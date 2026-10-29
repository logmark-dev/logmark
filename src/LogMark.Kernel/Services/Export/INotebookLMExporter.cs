namespace LogMark.Services.Export;

/// <summary>
/// Builds and exports a vault route as a single bundled markdown file suitable for upload as a NotebookLM source.
/// File-based export only.
/// </summary>
public interface INotebookLMExporter
{
    /// <summary>
    /// Build the bundle in memory and return content plus metadata, without writing to disk.
    /// Use this when the caller wants control over the destination (e.g. a SaveFileDialog).
    /// </summary>
    NotebookLMBundle BuildBundle(string vaultPath, string routeAbsolutePath);

    /// <summary>
    /// Build the bundle and write it to the default vault location at
    /// `{vaultPath}/_exports/notebooklm/{routeName}-{yyyyMMdd-HHmmss}.md`.
    /// </summary>
    /// <exception cref="ArgumentException">routeAbsolutePath is not under vaultPath, does not exist, or contains no .md files.</exception>
    NotebookLMExportResult ExportRoute(string vaultPath, string routeAbsolutePath);
}
