using System.IO;

namespace LogMark.Services.Routes;

/// <summary>
/// Edits a route's scalar fields (description, section, private preset) in place
/// in routes.md, preserving everything else in the block -- crucially the
/// `- scopes:` lines that the lossy RouteSerializer drops. Finishes with
/// RouteSetSerializer.Normalize for deterministic output.
/// </summary>
public static class RouteBlockEditor
{
    public static void UpdateScalarFields(
        string routesFilePath,
        string routePath,
        string? description,
        string? section,
        string? privatePreset)
    {
        EditBlock(routesFilePath, routePath, (lines, pathIdx, blockEnd) =>
        {
            UpsertField(lines, pathIdx + 1, blockEnd, "- description:", description);
            UpsertField(lines, pathIdx + 1, blockEnd, "- private:", privatePreset);
            // Section is a ## header relocation, not a block field; handled by the
            // editor's reparent/normalize path. Here we only touch in-block fields.
            return true;
        });
    }

    /// <summary>
    /// Rename a route's "### header" and update its description/status in place,
    /// preserving everything else in the block -- crucially scopes, private preset,
    /// and tags that the lossy RouteSerializer drops. The block is located by its
    /// "- path:" line (path is the stable key; the name may be changing). description
    /// and status are cleared when passed null/empty. Finishes with Normalize.
    /// </summary>
    public static void RenameAndUpdate(
        string routesFilePath,
        string routePath,
        string newName,
        string? description,
        string? status)
    {
        EditBlock(routesFilePath, routePath, (lines, pathIdx, blockEnd) =>
        {
            // Scan backward for this block's "### " header and rewrite the name.
            for (int i = pathIdx; i >= 0; i--)
            {
                var t = lines[i].TrimEnd();
                if (t.StartsWith("### "))
                {
                    if (!string.IsNullOrWhiteSpace(newName))
                        lines[i] = $"### {newName}";
                    break;
                }
                if (t.StartsWith("## "))
                    break; // crossed into the section header without finding a route header
            }

            UpsertField(lines, pathIdx + 1, blockEnd, "- description:", description);
            UpsertField(lines, pathIdx + 1, blockEnd, "- status:", status);
            return true;
        });
    }

    /// <summary>
    /// Set or clear a route's "- status:" line in place, preserving everything
    /// else in the block -- scopes, workdir bindings, private preset, tags -- that
    /// the lossy RouteSerializer drops. The block is located by its "- path:" line.
    /// Passing null/empty clears the status line (the canonical "active" state).
    /// Finishes with Normalize. This is the archive/restore write path; it must
    /// not rewrite the whole file, or every other route's workdir/scopes vanish.
    /// </summary>
    public static void UpdateStatus(
        string routesFilePath,
        string routePath,
        string? status)
    {
        EditBlock(routesFilePath, routePath, (lines, pathIdx, blockEnd) =>
        {
            UpsertField(lines, pathIdx + 1, blockEnd, "- status:", status);
            return true;
        });
    }

    /// <summary>
    /// Append a "- workdir: &lt;path&gt;" filesystem binding to a route's block,
    /// located by its "- path:" line. Idempotent: a binding already present in
    /// the block (case-insensitive) is left as-is. The line is inserted directly
    /// after "- path:" so it sits before any "- scopes:" block. Everything else
    /// in the file - other routes' workdirs, scopes, section notes - is preserved
    /// via Normalize. This is the editor counterpart to `lm link`, keyed by path
    /// rather than route name.
    /// </summary>
    public static void AddWorkdir(string routesFilePath, string routePath, string workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            return;
        var value = workdir.Trim();

        EditBlock(routesFilePath, routePath, (lines, pathIdx, blockEnd) =>
        {
            var workdirLine = $"- workdir: {value}";
            for (int i = pathIdx + 1; i < blockEnd && i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals(workdirLine, StringComparison.OrdinalIgnoreCase))
                    return false; // already linked; skip the rewrite entirely
            }

            lines.Insert(pathIdx + 1, workdirLine);
            return true;
        });
    }

    /// <summary>
    /// Remove a specific "- workdir: &lt;path&gt;" binding from a route's block,
    /// located by its "- path:" line. The match is exact on the trimmed line
    /// (case-insensitive). A no-op when the binding is absent. Everything else
    /// is preserved via Normalize.
    /// </summary>
    public static void RemoveWorkdir(string routesFilePath, string routePath, string workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
            return;
        var value = workdir.Trim();

        EditBlock(routesFilePath, routePath, (lines, pathIdx, blockEnd) =>
        {
            var workdirLine = $"- workdir: {value}";
            for (int i = pathIdx + 1; i < blockEnd && i < lines.Count; i++)
            {
                if (lines[i].Trim().Equals(workdirLine, StringComparison.OrdinalIgnoreCase))
                {
                    lines.RemoveAt(i);
                    break;
                }
            }
            return true;
        });
    }

    /// <summary>
    /// Shared edit frame: load routes.md, locate the route block by its
    /// "- path:" line, compute the block end (next "### "/"## " header), hand
    /// the lines to <paramref name="edit"/>, then Normalize and write back.
    /// The callback returns false to abandon the edit without touching the file.
    /// </summary>
    private static void EditBlock(
        string routesFilePath,
        string routePath,
        Func<List<string>, int, int, bool> edit)
    {
        if (!File.Exists(routesFilePath))
            return;

        var lines = File.ReadAllText(routesFilePath).Replace("\r\n", "\n").Split('\n').ToList();

        // Find the route block by its "- path:" line.
        int pathIdx = lines.FindIndex(l =>
            l.Trim().Equals($"- path: {routePath}", StringComparison.OrdinalIgnoreCase));
        if (pathIdx < 0)
            return;

        int blockEnd = pathIdx + 1;
        while (blockEnd < lines.Count &&
               !lines[blockEnd].TrimEnd().StartsWith("### ") &&
               !lines[blockEnd].TrimEnd().StartsWith("## "))
            blockEnd++;

        if (!edit(lines, pathIdx, blockEnd))
            return;

        var normalized = RouteSetSerializer.Normalize(string.Join("\n", lines));
        File.WriteAllText(routesFilePath, normalized);
    }

    /// <summary>Set or insert a "- field: value" line within [start,end); remove it when value is null/empty.</summary>
    private static void UpsertField(List<string> lines, int start, int end, string fieldPrefix, string? value)
    {
        int idx = -1;
        for (int i = start; i < end && i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith(fieldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            if (idx >= 0) lines.RemoveAt(idx);
            return;
        }

        var newLine = $"{fieldPrefix} {value}";
        if (idx >= 0) lines[idx] = newLine;
        else lines.Insert(start, newLine);
    }
}
