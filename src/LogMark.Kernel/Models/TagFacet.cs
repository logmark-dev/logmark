namespace LogMark.Models;

/// <summary>
/// A faceted tag (#key/value) split into its key (first segment), value
/// (everything after the first separator), and normalized full path.
/// Facets carry declared structure (aspect, status, phase); flat tags stay
/// thematic. Leaf extraction (NotationParser.ExtractTags) is unchanged -
/// facets are additive, extracted beside leaves, never instead of them.
/// </summary>
public record TagFacet(string Key, string Value, string FullPath);
