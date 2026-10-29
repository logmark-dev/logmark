namespace LogMark.Models;

/// <summary>
/// Base type for parsed content segments.
/// Used by ContentParser to break down entry content into renderable parts.
/// </summary>
public abstract record ContentSegment(int StartIndex, int Length, string RawText);

/// <summary>
/// Plain text segment (no special formatting).
/// </summary>
public sealed record TextSegment(int StartIndex, int Length, string RawText)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Wiki link segment: [[path]] or [[path|display]]
/// </summary>
/// <param name="Path">The link target path (without .md extension)</param>
/// <param name="DisplayText">The display text (null if no alias)</param>
/// <param name="IsPageLink">True if link points to a pages/ subpage</param>
public sealed record WikiLinkSegment(
    int StartIndex,
    int Length,
    string RawText,
    string Path,
    string? DisplayText,
    bool IsPageLink)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Tag segment: #tagname, or a faceted #key/value (#key:value on disk normalizes
/// to '/'). <paramref name="Tag"/> is the matched token verbatim, without the '#'.
/// </summary>
public sealed record TagSegment(int StartIndex, int Length, string RawText, string Tag)
    : ContentSegment(StartIndex, Length, RawText)
{
    /// <summary>
    /// The tag with ':' separators normalized to '/', matching
    /// NotationParser.ExtractTagPaths. This is the navigation identity: a facet
    /// tag navigates by full path, never by leaf.
    /// </summary>
    public string FullPath => Tag.Replace(':', '/');

    // First separator position in the normalized path. -1 (or a degenerate
    // leading/trailing position) means this is a flat tag, matching the
    // ExtractFacets contract exactly so display and index never disagree about
    // what counts as a facet.
    private int SeparatorIndex
    {
        get
        {
            var full = FullPath;
            var idx = full.IndexOf('/');
            return idx <= 0 || idx == full.Length - 1 ? -1 : idx;
        }
    }

    /// <summary>True when this tag carries a facet key and value.</summary>
    public bool IsFacet => SeparatorIndex > 0;

    /// <summary>Facet key (first segment), or null for a flat tag.</summary>
    public string? FacetKey => SeparatorIndex > 0 ? FullPath[..SeparatorIndex] : null;

    /// <summary>Facet value (everything after the first separator), or null for a flat tag.</summary>
    public string? FacetValue => SeparatorIndex > 0 ? FullPath[(SeparatorIndex + 1)..] : null;
}

/// <summary>
/// Project segment: +projectname
/// </summary>
public sealed record ProjectSegment(int StartIndex, int Length, string RawText, string Project)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Image segment: ![alt](path)
/// </summary>
public sealed record ImageSegment(int StartIndex, int Length, string RawText, string AltText, string Path)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Shroud masked-value placeholder, e.g. [AMOUNT:7F3A]. Rendered as a chip when locked,
/// revealed inline when session is unlocked.
/// </summary>
public sealed record TokenSegment(int StartIndex, int Length, string RawText, string EntityType, string TokenId)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Clickable URL segment. Scheme-allowlisted at parse time (https?, obsidian, mailto)
/// - custom URI handlers are an execution vector, so unknown schemes stay text.
/// </summary>
public sealed record UrlSegment(int StartIndex, int Length, string RawText, string Url)
    : ContentSegment(StartIndex, Length, RawText);

/// <summary>
/// Local filesystem path segment: bare drive-letter path, quoted/angle-bracketed
/// variant (captures spaces), or file:/// URI. LocalPath excludes the wrapping
/// quotes/brackets; RawText includes them.
/// </summary>
public sealed record PathSegment(int StartIndex, int Length, string RawText, string LocalPath)
    : ContentSegment(StartIndex, Length, RawText);
