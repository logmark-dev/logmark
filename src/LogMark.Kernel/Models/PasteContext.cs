namespace LogMark.Models;

/// <summary>
/// Context captured when user pastes content into LogMark.
/// </summary>
public class PasteContext : IDisposable
{
    public string? SourceUrl { get; set; }
    public string? SourceFilePath { get; set; }
    public int CharacterCount { get; set; }
    public int LineCount { get; set; }
    public bool HasImages { get; set; }

    /// <summary>
    /// The image from clipboard (direct paste), if any. Caller must dispose.
    /// </summary>
    public IImageData? ImageData { get; set; }

    /// <summary>
    /// Raw HTML content from CF_HTML clipboard format, if any.
    /// Used to extract embedded image URLs.
    /// </summary>
    public string? HtmlContent { get; set; }

    /// <summary>
    /// Markdown converted from HTML content.
    /// Preserves links, formatting, and image positions.
    /// </summary>
    public string? ConvertedMarkdown { get; set; }

    /// <summary>
    /// Plain text content from clipboard (for multi-line text pastes).
    /// Stored separately so prefix/suffix can be preserved.
    /// </summary>
    public string? PlainTextContent { get; set; }

    /// <summary>
    /// Image URLs extracted from converted markdown.
    /// </summary>
    public List<string> HtmlImageUrls { get; set; } = new();

    /// <summary>
    /// Full content to save as subpage (set when complex paste with images).
    /// VaultService will create subpage and replace entry with link.
    /// </summary>
    public string? SubpageContent { get; set; }

    /// <summary>
    /// Text the user typed before pasting.
    /// Used to preserve entry type notation (i:, t:, etc.) and project refs.
    /// </summary>
    public string? UserPrefix { get; set; }

    /// <summary>
    /// Text the user typed after pasting.
    /// Combined with prefix and converted markdown on submit.
    /// </summary>
    public string? UserSuffix { get; set; }

    /// <summary>
    /// Canonical entry-reference tokens pasted or prefilled as [link] placeholders,
    /// in placeholder order. Expanded back into the entry text at save time
    /// (after notation parsing - alias text must not affect routing).
    /// </summary>
    public List<string> LinkRefs { get; set; } = new();
    public bool HasLinkRef => LinkRefs.Count > 0;

    public bool HasSourceUrl => !string.IsNullOrEmpty(SourceUrl);
    public bool HasSourceFile => !string.IsNullOrEmpty(SourceFilePath);
    public bool HasHtmlImages => HtmlImageUrls.Count > 0;
    public bool HasConvertedMarkdown => !string.IsNullOrEmpty(ConvertedMarkdown);
    public bool HasPlainTextContent => !string.IsNullOrEmpty(PlainTextContent);
    public bool RequiresSubpage => !string.IsNullOrEmpty(SubpageContent);

    public void Dispose()
    {
        ImageData?.Dispose();
        ImageData = null;
    }
}
