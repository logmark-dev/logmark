namespace LogMark.Services.Vault;

/// <summary>
/// Lenient access to the vault.md manifest's frontmatter block, where a vault
/// declares the lm-vault format version it targets (<c>schema: lm-vault/1.0.0</c>,
/// SPEC-VERSION.md). Per the spec the block need not be valid YAML, so this reads
/// lines rather than parsing YAML, and the declaration is advisory: surfaced,
/// never enforced.
/// </summary>
public static class VaultManifestFrontmatter
{
    /// <summary>The format version new vaults declare.</summary>
    public const string CurrentSchema = "lm-vault/1.0.0";

    /// <summary>The declared <c>schema:</c> value, or null when the manifest declares none.</summary>
    public static string? ReadSchema(string? markdown)
    {
        var block = Extract(markdown);
        if (block is null) return null;

        foreach (var raw in block.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("schema:", StringComparison.Ordinal)) continue;
            var value = line["schema:".Length..].Trim().Trim('"', '\'');
            return value.Length > 0 ? value : null;
        }
        return null;
    }

    private const string SchemaPrefix = "lm-vault/";

    /// <summary>
    /// True when <paramref name="schema"/> is an lm-vault declaration whose MAJOR is
    /// later than the one this build implements. The spec forbids reading such a
    /// vault silently. A declaration under another prefix is not ours and an
    /// unparseable version declares nothing, so both read as supported.
    /// </summary>
    public static bool DeclaresUnsupportedMajor(string? schema) =>
        Major(schema) is int declared && Major(CurrentSchema) is int mine && declared > mine;

    /// <summary>A one-line warning for a vault declaring an unsupported MAJOR, or null.</summary>
    public static string? UnsupportedMajorWarning(string? schema) =>
        DeclaresUnsupportedMajor(schema)
            ? $"This vault declares {schema}; this build implements {CurrentSchema}. " +
              "Entries are read with the older rules and may be misread - do not rewrite the manifest or entries without the owner's confirmation."
            : null;

    /// <summary>
    /// <see cref="UnsupportedMajorWarning"/> for the manifest at <paramref name="manifestPath"/>.
    /// Best-effort for startup checks: a missing or unreadable manifest yields null.
    /// </summary>
    public static string? UnsupportedMajorWarningForFile(string manifestPath)
    {
        try
        {
            return File.Exists(manifestPath)
                ? UnsupportedMajorWarning(ReadSchema(File.ReadAllText(manifestPath)))
                : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static int? Major(string? schema)
    {
        if (schema is null || !schema.StartsWith(SchemaPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var version = schema[SchemaPrefix.Length..];
        var dot = version.IndexOf('.');
        return int.TryParse(dot < 0 ? version : version[..dot], out var major) ? major : null;
    }

    /// <summary>
    /// Returns <paramref name="incoming"/> with the frontmatter block of
    /// <paramref name="existing"/> prepended when incoming brings none of its own.
    /// A writer that replaces the manifest wholesale must not drop the version
    /// declaration of a caller that never saw it.
    /// </summary>
    public static string CarryForward(string? existing, string incoming)
    {
        if (Extract(incoming) is not null) return incoming;
        var block = Extract(existing);
        return block is null ? incoming : block + "\n" + incoming.TrimStart('\r', '\n');
    }

    /// <summary>
    /// The frontmatter block including both fences and the closing line break, or
    /// null. Matches the reader's rule: a <c>---</c> first line, closed by a
    /// <c>---</c> or <c>...</c> line.
    /// </summary>
    private static string? Extract(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return null;
        var text = markdown.TrimStart('﻿');

        var firstBreak = text.IndexOf('\n');
        if (firstBreak < 0 || text[..firstBreak].TrimEnd('\r', ' ', '\t') != "---") return null;

        var pos = firstBreak + 1;
        while (pos < text.Length)
        {
            var next = text.IndexOf('\n', pos);
            var end = next < 0 ? text.Length : next;
            var line = text[pos..end].TrimEnd('\r', ' ', '\t');
            if (line is "---" or "...")
                return next < 0 ? text + "\n" : text[..(next + 1)];
            if (next < 0) break;
            pos = next + 1;
        }
        return null;
    }
}
