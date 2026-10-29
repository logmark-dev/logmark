using LogMark.Services.Routes;
using LogMark.Services.EntryCache;

namespace LogMark.Services.Search;

/// <summary>
/// Additive subpage/page search that supplements the entry-index search on the
/// CLI and MCP surfaces. Long-form subpages (logmark/[route]/_[type]/pages/*.md)
/// carry no "## timestamp" header, so they parse to zero entries and never enter
/// the EntryCache-backed SearchService. This finds page bodies by case-insensitive
/// text match (optionally route-scoped) so a search can reach pasted/long-form
/// content, returned as a separate result set rather than shoehorned into the
/// entry shape - preserving the entry search's type/tag/scope filters (bead lmk-6asy).
/// </summary>
public static class SubpageSearch
{
    public static IReadOnlyList<SearchIndexRecord> Find(
        string? vaultPath, string? queryText, string? route, int limit)
    {
        if (string.IsNullOrWhiteSpace(vaultPath) || string.IsNullOrWhiteSpace(queryText))
            return Array.Empty<SearchIndexRecord>();

        try
        {
            var logmarkPath = Path.Combine(vaultPath, "logmark");
            var routes = new RouteLocator(new RouteParser(vaultPath).Parse().Routes);
            var source = new SubpageDocumentSource(new FileAccessor(), logmarkPath, () => routes);

            var results = new List<SearchIndexRecord>();
            foreach (var rec in source.GetRecords())
            {
                var hit = rec.SearchText.IndexOf(queryText, StringComparison.OrdinalIgnoreCase) >= 0
                    || rec.Title.IndexOf(queryText, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;

                // Parent-inclusive, like the entry route filter.
                if (!string.IsNullOrEmpty(route) && !routes.IsWithin(rec.Route, route))
                    continue;

                results.Add(rec);
                if (results.Count >= limit) break;
            }
            return results;
        }
        catch
        {
            // Best-effort supplement: never fail the primary search on a page-scan error.
            return Array.Empty<SearchIndexRecord>();
        }
    }
}
