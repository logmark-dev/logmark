using System.Text;
using System.Text.RegularExpressions;

namespace LogMark.Services.Organize;

/// <summary>
/// Produces a markdown document from an ExportScope.
/// Supports "raw" (content as-is) and "clean" (stripped prefixes, type labels) crystallization levels.
/// </summary>
public static class MarkdownExporter
{
    private static readonly Regex NotationPrefixRegex = new(
        @"^(t|i|b|d|q|g|a|task|idea|block|decision|question|gap|analogy):\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Export the given scope as a markdown document.
    /// </summary>
    /// <param name="scope">The export scope containing entries to render.</param>
    /// <param name="crystallization">"raw" or "clean".</param>
    public static string Export(ExportScope scope, string crystallization)
    {
        if (scope.EntryCount == 0)
            return "# LogMark Export\n\nNo entries in scope.\n";

        var isClean = string.Equals(crystallization, "clean", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("# LogMark Export");
        sb.AppendLine();
        sb.AppendLine($"{DateTime.Now:yyyy-MM-dd} - {scope.EntryCount} entries, {scope.RouteCount} routes, {scope.TypeCount} types");
        sb.AppendLine();

        // Group entries by route
        var groups = scope.Entries
            .GroupBy(e => e.Route ?? "(unrouted)", StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key == "(unrouted)" ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            sb.AppendLine($"# {group.Key}");
            sb.AppendLine();

            foreach (var scoped in group)
            {
                var ts = scoped.Entry.Timestamp;
                var content = scoped.Entry.Content;

                if (isClean)
                {
                    content = NotationPrefixRegex.Replace(content, "");
                    sb.AppendLine($"## {ts:yyyy-MM-dd HH:mm} [{scoped.Type}]");
                }
                else
                {
                    sb.AppendLine($"## {ts:yyyy-MM-dd HH:mm}");
                }

                sb.AppendLine();
                sb.AppendLine(content);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
