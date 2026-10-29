using System.Text.Json;

namespace LogMark.TestUtilities;

/// <summary>
/// Format-agnostic assertions over MCP tool-result JSON.
///
/// The MCP tool tests used to assert substrings of the PRETTY-PRINTED payload,
/// with a space after the colon that only exists when WriteIndented is on.
/// Bead lmk-1zld turned tool results compact - pretty-printing cost 10_843
/// bytes of pure whitespace on a measured 51KB get_project_context response,
/// 21% of the payload, which helped push it past a client's 20_000-byte
/// tool-result cap - and 91 assertions across 9 files broke at once even
/// though no behaviour had changed.
///
/// Asserting on serializer formatting was the actual defect. These helpers
/// parse instead, so the next serialization change is a non-event.
///
/// Deliberately free of FluentAssertions and xunit references: this project is
/// dependency-light on purpose (open substrate only), and a plain exception is
/// reported by any test framework just as clearly.
/// </summary>
public static class McpJsonAssertions
{
    /// <summary>
    /// Asserts the document contains a property with this name whose value
    /// equals <paramref name="expectedRawValue"/>, written as raw JSON
    /// (<c>true</c>, <c>3</c>, <c>"testproject"</c>). Searches nested objects
    /// and arrays, matching the reach of the substring assertions it replaces.
    /// </summary>
    public static void ShouldHaveJsonProperty(this string? json, string name, string expectedRawValue)
    {
        if (string.IsNullOrEmpty(json))
            throw new McpJsonAssertionException(
                $"expected a JSON payload containing '{name}', got {(json is null ? "null" : "empty string")}");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new McpJsonAssertionException(
                $"tool result was not valid JSON: {ex.Message}{Environment.NewLine}payload: {json}");
        }

        using (doc)
        {
            var actual = FindValues(doc.RootElement, name).ToList();
            if (actual.Count == 0)
                throw new McpJsonAssertionException(
                    $"no property named '{name}' anywhere in the payload.{Environment.NewLine}payload: {json}");

            var wanted = Canonical(expectedRawValue);
            if (!actual.Any(a => Canonical(a) == wanted))
                throw new McpJsonAssertionException(
                    $"property '{name}' was [{string.Join(", ", actual)}], expected {wanted}."
                    + $"{Environment.NewLine}payload: {json}");
        }
    }

    /// <summary>Asserts no property with this name carries this value.</summary>
    public static void ShouldNotHaveJsonProperty(this string? json, string name, string expectedRawValue)
    {
        if (string.IsNullOrEmpty(json)) return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return; }

        using (doc)
        {
            var wanted = Canonical(expectedRawValue);
            if (FindValues(doc.RootElement, name).Any(a => Canonical(a) == wanted))
                throw new McpJsonAssertionException(
                    $"expected NO property '{name}' with value {wanted}, but found one."
                    + $"{Environment.NewLine}payload: {json}");
        }
    }

    private static IEnumerable<string> FindValues(JsonElement element, string name)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.NameEquals(name))
                        yield return prop.Value.GetRawText();
                    foreach (var nested in FindValues(prop.Value, name))
                        yield return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in FindValues(item, name))
                        yield return nested;
                break;
        }
    }

    /// <summary>
    /// Reduces a raw JSON scalar to a canonical form so formatting, numeric
    /// spelling, and escaping do not affect equality. Falls back to the
    /// trimmed literal for anything unparseable.
    /// </summary>
    private static string Canonical(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            return rawJson.Trim();
        }
    }
}

/// <summary>Signals a failed MCP JSON assertion.</summary>
public class McpJsonAssertionException : Exception
{
    public McpJsonAssertionException(string message) : base(message) { }
}
