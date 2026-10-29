using System.Text.Json;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Mints missing block anchors in one entry file. Fix verb "mint-anchors".
/// Payload: {"path"} - vault-relative, as emitted by MissingEntryAnchorLint.
///
/// Scoped to a single file rather than delegating to AnchorMintSweep.Apply:
/// a batch fix walks issues one at a time, and a whole-vault sweep behind the
/// first of them would report every sibling issue fixed without being asked.
///
/// No write gate, matching LintCommandSupport's headless fixer wiring - minting
/// an anchor introduces no new plaintext that would need private-route masking,
/// it only stamps an id onto content already on disk.
/// </summary>
public class MintAnchorsFixer : ILintFixer
{
    public string Id => "mint-anchors";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly IVaultFileWriter? _writer;

    public MintAnchorsFixer(IVaultFileWriter? writer = null) => _writer = writer;

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "Empty payload", null));

        Payload? p;
        try { p = JsonSerializer.Deserialize<Payload>(issue.FixPayloadJson, _json); }
        catch (JsonException ex) { return Task.FromResult(new LintFixResult(false, $"Malformed payload: {ex.Message}", null)); }

        if (p == null || string.IsNullOrWhiteSpace(p.Path))
            return Task.FromResult(new LintFixResult(false, "Payload missing path", null));

        var vaultRoot = Path.GetFullPath(ctx.VaultPath);
        var absolute = Path.GetFullPath(Path.Combine(vaultRoot, p.Path));
        if (!absolute.StartsWith(vaultRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new LintFixResult(false, $"Path '{p.Path}' resolves outside the vault", null));

        var minted = new AnchorMintSweep(ctx.VaultPath, writer: _writer).ApplyToFile(absolute);

        // Report what was verified, not what was attempted. Zero minted means
        // the file was already converged (or absent) - the issue was stale, and
        // calling that a fix is how a batch reports successes it never earned.
        if (minted == 0)
            return Task.FromResult(new LintFixResult(false, $"No anchors were minted in {p.Path}", null));

        // Minting changes the addressable id of every entry it touched, so the
        // cached parse of this file is now wrong in the one way that matters.
        ctx.Entries.InvalidateFile(absolute);

        return Task.FromResult(new LintFixResult(
            true, null, $"Minted {minted} anchor(s) in {p.Path}"));
    }

    private record Payload(string Path);
}
