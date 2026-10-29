namespace LogMark.Services.Export;

/// <summary>
/// Result of a NotebookLM export operation.
/// </summary>
/// <param name="BundlePath">Absolute path to the written bundle markdown file.</param>
/// <param name="SourceFileCount">Number of vault .md files included in the bundle.</param>
/// <param name="WordCount">Approximate total word count of the bundled content.</param>
public record NotebookLMExportResult(
    string BundlePath,
    int SourceFileCount,
    int WordCount);
