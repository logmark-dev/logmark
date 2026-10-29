namespace LogMark.Services.Agents;

/// <summary>
/// Claude Code keys local-scope MCP entries in ~/.claude.json under
/// projects["&lt;key&gt;"]: the working-directory string its session started
/// in, with forward slashes and no trailing separator. A terminal session
/// gives an uppercase drive letter, and Claude Code 2.1.306 does not match a
/// lowercase-drive key from there (rc6 QA, 2026-10-21), so the canonical key
/// uses the uppercase drive. Editor launches can still produce other
/// spellings; ClaudeLocalScopeWriter also serves those case variants. On Unix
/// the path is already in that shape apart from trailing separators.
/// </summary>
public static class ClaudeProjectKey
{
    public static string Normalize(string projectDir)
    {
        var full = Path.GetFullPath(projectDir).Replace('\\', '/').TrimEnd('/');
        if (full.Length >= 2 && full[1] == ':' && char.IsLetter(full[0]))
            full = char.ToUpperInvariant(full[0]) + full[1..];
        return full;
    }

    /// <summary>
    /// True when <paramref name="key"/> names the same directory as the
    /// canonical key: equal, or (on Windows, whose paths ignore case) equal
    /// apart from case.
    /// </summary>
    public static bool SameDirectory(string key, string canonical) =>
        string.Equals(key, canonical,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
