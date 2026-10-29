using System.Net;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Read-only subpage resolver for GET /api/pages (lmk-nhjr Task 2): serves
/// the markdown file a wikilink addresses. Port of the LogMark.Knowledge
/// Node cockpit's route (LogMark.Knowledge/src/server/routes/pages.ts) -
/// same candidate-path generation, same double traversal guard, same
/// response shapes, so the unmodified cockpit SPA can read subpages through
/// the daemon exactly as it does through the Node server.
///
/// Wikilinks use logical folder names (`blocks/pages/...`); the vault
/// stores `_blocks/pages/...`, so both spellings are tried
/// (<see cref="CandidatePagePaths"/>). Path traversal is rejected twice:
/// candidate generation refuses a `..` path segment, and the resolved
/// absolute path must stay under the vault content root
/// (<see cref="VaultContentRoot"/>).
///
/// One deliberate shape divergence from the TS route: the TS route answers
/// an empty/`..` path with a plain `{ error: 'invalid_path' }` 400 body
/// (candidatePagePaths returns no candidates before any file I/O happens).
/// The cockpit's fetch wrapper (src/ui/api.ts) only special-cases status
/// 404 - any other non-2xx throws a generic `Error` before the body is ever
/// parsed - so nothing in the SPA reads that shape. This port raises the
/// existing RFC 9457 <see cref="V0Routes.WriteProblem"/> for that case
/// instead, matching every other error path in the daemon's compat surface.
/// The found (200) and not-found (404) shapes, which the SPA does parse
/// (`{ path, body }` / `{ error: 'not_found' }`), are ported byte-for-byte.
/// </summary>
public static class CockpitPageResolver
{
    /// <summary>
    /// Port of pages.ts's candidatePagePaths. Returns the candidate
    /// vault-relative paths to try, in preference order, or an empty list
    /// when the input is empty or contains a `..` path segment (checked
    /// over both `/` and `\` separators, before backslash normalization).
    /// </summary>
    public static IReadOnlyList<string> CandidatePagePaths(string? rel)
    {
        var clean = (rel ?? string.Empty).Trim().TrimStart('/');
        if (clean.Length == 0 || clean.Split('\\', '/').Contains(".."))
            return Array.Empty<string>();

        // Normalize backslashes AFTER the traversal check (which already
        // looked at both separators), matching the TS comment: a leading
        // backslash becomes a leading forward slash here, which the
        // prefix guard in Handle() still rejects on the resolved path.
        var normalized = clean.Replace('\\', '/');
        var withExt = normalized.EndsWith(".md", StringComparison.Ordinal)
            ? normalized
            : normalized + ".md";

        var result = new List<string> { withExt };
        var parts = withExt.Split('/');
        var pagesIdx = Array.IndexOf(parts, "pages");
        if (pagesIdx > 0 && !parts[pagesIdx - 1].StartsWith("_", StringComparison.Ordinal))
        {
            var variant = (string[])parts.Clone();
            variant[pagesIdx - 1] = "_" + variant[pagesIdx - 1];
            result.Add(string.Join('/', variant));
        }
        return result;
    }

    /// <summary>
    /// Bare-slug fallback (rc6): a wiki link like [[my-page]] carries no
    /// folder. Searches the content root for a file named
    /// <paramref name="fileName"/> (case-insensitive), skipping "_system",
    /// dot folders, and reparse points. Ranking mirrors the Node cockpit's
    /// comparePageMatches: a path with a "pages" or "_pages" segment first,
    /// then fewer segments, then ordinal on the forward-slash path.
    /// Returns the forward-slash path relative to the root, or null.
    /// </summary>
    public static string? FindBySlug(string rootDir, string fileName)
    {
        var matches = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootDir);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            string[] files, dirs;
            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                if (!string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                // A file symlink would let ReadAllText follow it out of the root.
                if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                matches.Add(Path.GetRelativePath(rootDir, file).Replace('\\', '/'));
            }

            foreach (var sub in dirs)
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || name == "_system") continue;
                if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                pending.Push(sub);
            }
        }

        return matches
            .OrderBy(p => p.Split('/').Any(s => s is "pages" or "_pages") ? 0 : 1)
            .ThenBy(p => p.Split('/').Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static bool Handle(HttpListenerContext ctx, string vaultPath)
    {
        var rel = ctx.Request.QueryString["path"] ?? string.Empty;
        var candidates = CandidatePagePaths(rel);
        if (candidates.Count == 0)
        {
            return V0Routes.WriteProblem(ctx, 400, "invalid-path", "Invalid path",
                "path must not be empty or contain '..' segments.");
        }

        var rootDir = Path.GetFullPath(VaultContentRoot.Resolve(vaultPath));
        var prefix = rootDir + Path.DirectorySeparatorChar;

        foreach (var candidate in candidates)
        {
            var abs = Path.GetFullPath(Path.Combine(rootDir, candidate));
            if (!abs.StartsWith(prefix, StringComparison.Ordinal)) continue;

            try
            {
                var body = File.ReadAllText(abs);
                return V0Routes.WriteJson(ctx, new { path = candidate, body });
            }
            catch (IOException)
            {
                // Not found (or unreadable) under this candidate - try the next.
            }
            catch (UnauthorizedAccessException)
            {
                // Same treatment as IOException: try the next candidate.
            }
        }

        // A bare slug (no folder) gets one search of the content root.
        if (!candidates[0].Contains('/') && FindBySlug(rootDir, candidates[0]) is { } hit)
        {
            var abs = Path.GetFullPath(Path.Combine(rootDir, hit));
            if (abs.StartsWith(prefix, StringComparison.Ordinal))
            {
                try { return V0Routes.WriteJson(ctx, new { path = hit, body = File.ReadAllText(abs) }); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        return V0Routes.WriteJson(ctx, new { error = "not_found" }, 404);
    }
}
