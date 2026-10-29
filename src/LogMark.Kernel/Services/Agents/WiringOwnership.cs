using System.Text.Json;
using LogMark.Services.Providers;

namespace LogMark.Services.Agents;

/// <summary>What the caller observed about one wired entry before deciding.</summary>
public sealed record EntryProbe(bool Exists, string? Command, string? EntryVault, bool Broken = false);

/// <summary>The parts of a wired MCP entry the ownership contract reads.</summary>
public sealed record McpEntryInfo(string? Command, string? BootstrapPath, string? VaultOverride);

/// <summary>
/// Turns an observed entry into WiringFacts (origin, owners, vault, winner)
/// and records the outcome of a decided action in the ownership ledger
/// (root design 2026-10-21 sections 3.1-3.2). Hooks and MCP only; skills
/// read owners from their marker via FactsFromOwners and record nothing here.
/// </summary>
public sealed class WiringOwnership
{
    private static readonly HashSet<string> LogMarkBinaryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "logmark", "logmark.exe", "logmark-mcp", "logmark-mcp.exe", "LogMark.Mcp", "LogMark.Mcp.exe",
        "logmark-daemon", "logmark-daemon.exe", "lm", "lm.exe", "logmark-run", "logmark-run.cmd"
    };

    private readonly WiringOwnershipStore _store;
    private readonly ProviderManifestStore _manifests;
    private readonly Func<string, bool> _fileExists;

    public WiringOwnership(string callerId, WiringOwnershipStore store, ProviderManifestStore manifests,
        Func<string, bool>? fileExists = null)
    {
        CallerId = callerId;
        _store = store;
        _manifests = manifests;
        _fileExists = fileExists ?? File.Exists;
    }

    public string CallerId { get; }

    public static WiringOwnership Default(string callerId) => new(callerId,
        new WiringOwnershipStore(WiringOwnershipStore.DefaultRoot()),
        new ProviderManifestStore(ProviderManifestStore.DefaultRoot()));

    public WiringFacts Facts(WiringTarget target, EntryProbe probe, string role)
    {
        var winner = Winner(role);
        if (!probe.Exists)
            return new WiringFacts(EntryOrigin.None, Array.Empty<string>(), null, winner);
        if (!IsLogMarkCommand(probe.Command))
            return new WiringFacts(EntryOrigin.Custom, Array.Empty<string>(), probe.EntryVault, winner);

        var record = _store.Load(target);
        var owners = record is null ? LegacyOwners(probe.Command, role, winner) : Widen(record.Owners, probe.Command, role);
        return new WiringFacts(EntryOrigin.LogMark, owners, probe.EntryVault, winner, probe.Broken,
            WrittenByNewerVersion: record is not null && record.OwnershipVersion > WiringOwnershipRecord.CurrentVersion);
    }

    /// <summary>
    /// A record can trail the entry: the desktop's self-heal rewrites the
    /// command to its own binary without touching the ledger. When the entry
    /// runs a live provider's binary for this role and that provider is not
    /// recorded, it owns the entry too (effective only; persisted by the next
    /// Apply). Final review Important 2. Never the caller itself: a record that
    /// lost the caller (the documented write race) keeps the caller's
    /// disconnect a no-op.
    /// </summary>
    private IReadOnlyList<string> Widen(IReadOnlyList<string> recorded, string? command, string role)
    {
        if (string.IsNullOrEmpty(command)) return recorded;
        var runner = _manifests.LoadAll().FirstOrDefault(m =>
            m.Binaries.TryGetValue(role, out var b) && PathsEqual(b.Path, command) && _fileExists(b.Path));
        return runner is null || runner.ProviderId == CallerId || recorded.Contains(runner.ProviderId)
            ? recorded
            : recorded.Append(runner.ProviderId).ToList();
    }

    /// <summary>Skills: null owners = not installed; empty = legacy marker.</summary>
    public WiringFacts FactsFromOwners(IReadOnlyList<string>? owners, string role, bool broken = false)
    {
        var winner = Winner(role);
        if (owners is null)
            return new WiringFacts(EntryOrigin.None, Array.Empty<string>(), null, winner);
        var effective = owners.Count > 0 ? owners : LegacyOwners(null, role, winner);
        return new WiringFacts(EntryOrigin.LogMark, effective, null, winner, broken);
    }

    public (WiringFacts Facts, WiringDecisionResult Decision) DecideDisconnect(
        WiringTarget target, EntryProbe probe, string role, string subject, WiringOverrides overrides)
    {
        var facts = Facts(target, probe, role);
        return (facts, WiringDecision.Decide(WiringVerb.Disconnect, CallerId, facts, null, overrides, subject, offerLocalScope: false));
    }

    public (WiringFacts Facts, WiringDecisionResult Decision) DecideSkillsDisconnect(
        IReadOnlyList<string>? owners, string subject, WiringOverrides overrides)
    {
        var facts = FactsFromOwners(owners, "cli");
        return (facts, WiringDecision.Decide(WiringVerb.Disconnect, CallerId, facts, null, overrides, subject, offerLocalScope: false));
    }

    public void Apply(WiringTarget target, WiringFacts facts, WiringAction action)
    {
        switch (action)
        {
            case WiringAction.Create:
                _store.Save(target, new[] { CallerId });
                break;
            case WiringAction.AddOwner:
            case WiringAction.Rewrite:
                _store.Save(target, facts.Owners.Append(CallerId));
                break;
            case WiringAction.RemoveOwner:
                _store.Save(target, facts.Owners.Where(o => o != CallerId));
                break;
            case WiringAction.RemoveEntry:
                _store.Delete(target);
                break;
        }
    }

    /// <summary>
    /// The vault an MCP entry pins: its --vault-override, the only tier that
    /// beats a live host's ActiveVault in the MCP server's resolution. Null
    /// when the entry carries none. A --bootstrap-path is not a pin (see
    /// <see cref="BootstrapFileVault"/>).
    /// </summary>
    public static string? PinnedVault(McpEntryInfo info) =>
        string.IsNullOrEmpty(info.VaultOverride) ? null : info.VaultOverride;

    /// <summary>
    /// The vault the entry's own --bootstrap-path file names: a file named
    /// vault-bootstrap.json sits in its vault; any other bootstrap file is read
    /// for its VaultPath. Null when the entry has no bootstrap path or the file
    /// cannot be read.
    /// </summary>
    public static string? BootstrapFileVault(McpEntryInfo info)
    {
        if (string.IsNullOrEmpty(info.BootstrapPath)) return null;
        if (string.Equals(Path.GetFileName(info.BootstrapPath), "vault-bootstrap.json", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(info.BootstrapPath);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(info.BootstrapPath));
            return doc.RootElement.TryGetProperty("VaultPath", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// The vault an agent's MCP server reaches through this entry, in the MCP
    /// server's own order (LogMark.Mcp Program.Run): --vault-override; else
    /// the live host's ActiveVault; else the entry's bootstrap file; else the
    /// app bootstrap. Callers pass the live-host and app-bootstrap lookups so
    /// tests can stub them.
    /// </summary>
    public static string? EntryVault(McpEntryInfo info, Func<string?> liveHostVault, Func<string?> appBootstrapVault) =>
        PinnedVault(info) ?? liveHostVault() ?? BootstrapFileVault(info) ?? appBootstrapVault();

    /// <summary>The vault the entry names itself (override, else its bootstrap
    /// file), without live-host resolution. Null when it names none.</summary>
    public static string? NamedVault(McpEntryInfo info) => PinnedVault(info) ?? BootstrapFileVault(info);

    public bool IsLogMarkCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        if (LogMarkBinaryNames.Contains(Path.GetFileName(command))) return true;
        return _manifests.LoadAll().Any(m => m.Binaries.Values.Any(b => PathsEqual(b.Path, command)));
    }

    private string? Winner(string role) =>
        new ProviderResolver(_manifests, _fileExists).Resolve(role)?.Manifest.ProviderId;

    private IReadOnlyList<string> LegacyOwners(string? command, string role, string? winner)
    {
        if (!string.IsNullOrEmpty(command))
        {
            var match = _manifests.LoadAll().FirstOrDefault(m =>
                m.Binaries.TryGetValue(role, out var b) && PathsEqual(b.Path, command));
            if (match is not null) return new[] { match.ProviderId };
        }
        // No manifest owns this exact binary: prefer a registered producer
        // install (the desktop app) that is really on disk, then the role
        // winner, then the caller. A producer that declares no binary for this
        // role must still own its own legacy entries.
        var producer = _manifests.LoadAll().FirstOrDefault(m =>
            m.Capabilities.Producers && m.Binaries.Values.Any(b => _fileExists(b.Path)));
        if (producer is not null) return new[] { producer.ProviderId };
        return new[] { winner ?? CallerId };
    }

    private static bool PathsEqual(string a, string b) => string.Equals(
        a.Replace('/', '\\').TrimEnd('\\'), b.Replace('/', '\\').TrimEnd('\\'),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
