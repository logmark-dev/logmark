namespace LogMark.Services.Export;

/// <summary>
/// In-memory bundle ready for write. Exposes content plus metadata so callers can
/// either save to a default vault location or prompt the user for a destination.
/// </summary>
/// <param name="Content">Full bundle markdown text.</param>
/// <param name="SourceFileCount">Number of source .md files concatenated into the bundle.</param>
/// <param name="WordCount">Approximate total word count of the bundle content.</param>
/// <param name="SuggestedFileName">Default filename for the bundle, e.g. "LogMark-20260504-152500.md".</param>
/// <param name="DefaultDirectory">Recommended directory under the vault where bundles are stored.</param>
public record NotebookLMBundle(
    string Content,
    int SourceFileCount,
    int WordCount,
    string SuggestedFileName,
    string DefaultDirectory);
