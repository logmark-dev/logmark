using System.Text;

namespace LogMark.Services.Export;

// ITimeProvider lives in LogMark.Services and is visible without an explicit using
// because LogMark.Services.Export sits under it.
public class NotebookLMExporter : INotebookLMExporter
{
    private const string ExportSubdir = "_exports/notebooklm";

    private readonly ITimeProvider _time;

    public NotebookLMExporter(ITimeProvider time)
    {
        _time = time;
    }

    public NotebookLMBundle BuildBundle(string vaultPath, string routeAbsolutePath)
    {
        var normalizedVault = Path.GetFullPath(vaultPath);
        var normalizedRoute = Path.GetFullPath(routeAbsolutePath);

        var vaultRoot = normalizedVault.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var routeFull = normalizedRoute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var isVault = string.Equals(routeFull, vaultRoot, StringComparison.OrdinalIgnoreCase);
        var isUnderVault = routeFull.StartsWith(vaultRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        if (!isVault && !isUnderVault)
            throw new ArgumentException("routeAbsolutePath must be inside vaultPath", nameof(routeAbsolutePath));

        if (!Directory.Exists(normalizedRoute))
            throw new ArgumentException($"Route folder does not exist: {normalizedRoute}", nameof(routeAbsolutePath));

        var exportRoot = Path.Combine(vaultRoot, "_exports");
        var files = Directory.EnumerateFiles(normalizedRoute, "*.md", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(exportRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            throw new ArgumentException($"Route contains no .md files: {normalizedRoute}", nameof(routeAbsolutePath));

        var routeName = Path.GetFileName(normalizedRoute);
        if (string.IsNullOrEmpty(routeName))
            routeName = "vault";
        var timestamp = _time.Now;
        var sb = new StringBuilder();

        AppendHeader(sb, routeName, files.Count, timestamp);

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(normalizedVault, file).Replace('\\', '/');
            sb.AppendLine($"## {rel}");
            sb.AppendLine();
            sb.AppendLine(File.ReadAllText(file));
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        var content = sb.ToString();
        var wordCount = CountWords(content);
        var suggestedFileName = $"{routeName}-{timestamp:yyyyMMdd-HHmmss}.md";
        var defaultDirectory = Path.GetFullPath(Path.Combine(normalizedVault, ExportSubdir));

        return new NotebookLMBundle(content, files.Count, wordCount, suggestedFileName, defaultDirectory);
    }

    public NotebookLMExportResult ExportRoute(string vaultPath, string routeAbsolutePath)
    {
        var bundle = BuildBundle(vaultPath, routeAbsolutePath);
        Directory.CreateDirectory(bundle.DefaultDirectory);
        var bundlePath = Path.Combine(bundle.DefaultDirectory, bundle.SuggestedFileName);
        File.WriteAllText(bundlePath, bundle.Content);
        return new NotebookLMExportResult(bundlePath, bundle.SourceFileCount, bundle.WordCount);
    }

    // Keep in sync with docs/architecture/notation-spec.md.
    private static void AppendHeader(StringBuilder sb, string routeName, int fileCount, DateTime timestamp)
    {
        sb.AppendLine($"# LogMark Vault Export: +{routeName}");
        sb.AppendLine();
        sb.AppendLine($"Generated: {timestamp:yyyy-MM-ddTHH:mm:ss} (local time)");
        sb.AppendLine($"Files: {fileCount} markdown files from route folder.");
        sb.AppendLine();
        sb.AppendLine("## Notation Reference");
        sb.AppendLine();
        sb.AppendLine("Each entry uses prefixes for type:");
        sb.AppendLine("- t: task (actionable)");
        sb.AppendLine("- i: idea (thought worth preserving)");
        sb.AppendLine("- b: block (explicitly stuck)");
        sb.AppendLine("- d: decision (choice made, often #frozen)");
        sb.AppendLine("- q: question (open inquiry)");
        sb.AppendLine("- g: gap (knowledge debt)");
        sb.AppendLine("- a: analogy (structural mapping)");
        sb.AppendLine("- no prefix: log entry / diary");
        sb.AppendLine();
        sb.AppendLine("Tags (#tag) are cross-cutting concerns. Routes (+route) are the project namespace.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static int CountWords(string content)
    {
        return content.Split(
            new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
