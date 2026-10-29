using System.Text.Json;

namespace LogMark.Services;

/// <summary>Result of comparing an on-disk vault stamp against this build's understood format.</summary>
public enum VaultStampState
{
    /// <summary>Stamp matches or predates this build's <see cref="VaultStamp.CurrentFormat"/>.</summary>
    Ok,
    /// <summary>No stamp file exists yet (pre-stamp vault, or never seeded).</summary>
    Missing,
    /// <summary>The stamp records a format newer than this build understands.</summary>
    NewerThanMine,
}

/// <summary>On-disk contents of the vault format stamp.</summary>
public sealed record VaultStampInfo(int VaultFormat, string SeededBy, DateTimeOffset SeededAt);

/// <summary>
/// Vault format stamp: <c>&lt;logmarkPath&gt;/_system/vault.json</c> records the vault
/// structure format and which product last wrote/backfilled it. Companion to
/// <see cref="VaultInitializer"/>'s seed-if-absent semantics - without this, the first
/// product to ever touch a vault silently freezes its on-disk shape, with no version
/// marker anywhere for a later product (or a later, incompatible build of the same
/// product) to read and respect.
///
/// <see cref="EnsureWritten"/> is the only writer: it backfills a stamp for vaults that
/// predate this file, and never downgrades an existing stamp with a format newer than
/// <see cref="CurrentFormat"/>. This stamp is private to this implementation and is not
/// the lm-vault format version - that is the manifest's <c>schema:</c> declaration
/// (<see cref="Vault.VaultManifestFrontmatter"/>). <see cref="Check"/> is read-only - it never writes a
/// stamp - so callers can safely gate migrations or re-seeding on its result without
/// side effects.
/// </summary>
public static class VaultStamp
{
    /// <summary>The vault structure format this build understands and writes.</summary>
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly JsonSerializerOptions ReadOptions = new()
    { PropertyNameCaseInsensitive = true };

    private sealed record StampFile(int VaultFormat, string SeededBy, DateTimeOffset SeededAt);

    /// <summary>Path to the stamp file under a vault's <c>logmark</c> content root.</summary>
    public static string PathFor(string logmarkPath) =>
        Path.Combine(logmarkPath, "_system", "vault.json");

    /// <summary>Reads the stamp, or null when absent or unparseable (lenient - never throws).</summary>
    public static VaultStampInfo? Read(string logmarkPath)
    {
        var path = PathFor(logmarkPath);
        if (!File.Exists(path))
            return null;

        try
        {
            var stamp = JsonSerializer.Deserialize<StampFile>(File.ReadAllText(path), ReadOptions);
            return stamp is null ? null : new VaultStampInfo(stamp.VaultFormat, stamp.SeededBy, stamp.SeededAt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the stamp at <see cref="CurrentFormat"/> if absent, or backfills a
    /// pre-stamp vault. No-op when the existing stamp already records a format
    /// greater than or equal to <see cref="CurrentFormat"/> - this build never
    /// downgrades a stamp written by a newer product.
    /// </summary>
    public static void EnsureWritten(string logmarkPath, string seededBy)
    {
        var existing = Read(logmarkPath);
        if (existing is not null && existing.VaultFormat >= CurrentFormat)
            return;

        var path = PathFor(logmarkPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var stamp = new StampFile(CurrentFormat, seededBy, DateTimeOffset.UtcNow);

        // Atomic write: write to a temp file then rename over the target. A
        // mid-write crash on a direct WriteAllText could leave a truncated or
        // corrupt stamp file that Read's lenient try/catch treats as absent -
        // and a later EnsureWritten call would then overwrite whatever format
        // was actually recorded, violating the never-downgrade contract.
        var tempPath = path + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(stamp, WriteOptions));
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Read-and-respect check: never writes. Missing when no stamp exists yet.</summary>
    public static VaultStampState Check(string logmarkPath)
    {
        var stamp = Read(logmarkPath);
        if (stamp is null)
            return VaultStampState.Missing;

        return stamp.VaultFormat > CurrentFormat ? VaultStampState.NewerThanMine : VaultStampState.Ok;
    }
}
