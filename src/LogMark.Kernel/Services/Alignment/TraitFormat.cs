using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogMark.Services.EntryParser;

namespace LogMark.Services.Alignment;

/// <summary>
/// The markdown shape of a trait entry. Human-readable and hand-editable; a
/// trait that no longer parses is skipped, never fatal (design sec 5.1).
///
/// Real EntryParser behaviour this format is built around: after the first
/// content line, lines matching ^[a-z_]+: (.+)$ are lifted into
/// ExtendedMetadata (scope, evidence, reasons). A line ending in "(...)" is
/// tried as a workflow line first and, when not a duration, falls through to
/// Content - so "status: confirmed (date)" stays in Content. Hyphenated keys
/// (baseline-discard-rate) and "examples (rejected):" never match the metadata
/// pattern and stay in Content, as do the indented "- " example lines.
/// Parse therefore reads each field from metadata first, then from the body.
/// </summary>
public static class TraitFormat
{
    public const string Tag = "trait";
    public const string RouteName = "self";
    public const int PromptCap = 8;
    public const int MaxExampleChars = 160;
    private const string ExamplesHeader = "examples (rejected):";

    public static string Render(LearnedTrait t)
    {
        var sb = new StringBuilder();
        sb.Append(NeutralizeField(t.Rule)).Append(" #").AppendLine(Tag);
        sb.Append("scope: ").AppendLine(NeutralizeField(t.Scope));
        sb.Append("status: ").Append(t.Status == TraitStatus.Confirmed ? "confirmed" : "retired")
          .Append(" (").Append(t.StatusDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).AppendLine(")");
        sb.Append("evidence: ").AppendLine(NeutralizeField(t.EvidenceLine));
        if (!string.IsNullOrWhiteSpace(t.ReasonsLine)) sb.Append("reasons: ").AppendLine(NeutralizeField(t.ReasonsLine));
        if (t.BaselineDiscardRate is { } b)
            sb.Append("baseline-discard-rate: ").AppendLine(b.ToString("0.00", CultureInfo.InvariantCulture));
        if (t.Examples.Count > 0)
        {
            sb.AppendLine(ExamplesHeader);
            foreach (var e in t.Examples) sb.Append("  - ").AppendLine(NeutralizeExample(e));
        }
        return sb.ToString().TrimEnd();
    }

    public static string NeutralizeExample(string text)
    {
        var s = Regex.Replace(text ?? "", @"(^|\s)\+[\w\-/]+", " ");     // +route tokens
        s = s.Replace("#", "").Replace("\"", "").Replace("`", "");
        s = SingleLine(s);
        return s.Length <= MaxExampleChars ? s : s[..(MaxExampleChars - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// Makes free text safe as a rule/field value inside an entry: single line,
    /// no +route or #tag tokens, no heading marker, no leading type prefix (t:, gap:, ...).
    /// No length cap (rules may be longer than examples).
    /// </summary>
    private static string NeutralizeField(string? text)
    {
        var s = Regex.Replace(text ?? "", @"(^|\s)\+[\w\-/]+", " ");
        s = SingleLine(s.Replace("#", ""));
        while (EntryPrefix.StripLeading(s) is { } rest && rest != s) s = rest.Trim();
        return s;
    }

    private static string SingleLine(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    public static LearnedTrait? Parse(string? shortId, string content, IReadOnlyDictionary<string, string>? meta)
    {
        try
        {
            return ParseCore(shortId, content, meta);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            return null; // hand-edited garbage is skipped, never fatal
        }
    }

    private static LearnedTrait? ParseCore(string? shortId, string content, IReadOnlyDictionary<string, string>? meta)
    {
        if (string.IsNullOrWhiteSpace(shortId) || string.IsNullOrWhiteSpace(content)) return null;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var first = lines[0].Trim();
        if (!Regex.IsMatch(first, @"(^|\s)#" + Tag + @"\b", RegexOptions.IgnoreCase)) return null;
        var rule = NeutralizeField(Regex.Replace(first, @"(^|\s)#" + Tag + @"\b", " ", RegexOptions.IgnoreCase));
        if (rule.Length == 0) return null;

        string? Field(string key)
        {
            if (meta is not null && meta.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
            var line = lines.Skip(1).FirstOrDefault(l => l.TrimStart().StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
            return line is null ? null : line[(line.IndexOf(':') + 1)..].Trim();
        }

        var scope = Field("scope");
        var statusRaw = Field("status");
        if (string.IsNullOrWhiteSpace(scope) || statusRaw is null) return null;
        var m = Regex.Match(statusRaw, @"^(confirmed|retired)\s*\((\d{4}-\d{2}-\d{2})\)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var status = m.Groups[1].Value.Equals("confirmed", StringComparison.OrdinalIgnoreCase) ? TraitStatus.Confirmed : TraitStatus.Retired;
        if (!DateTime.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        var evidence = Field("evidence") ?? "";
        var countMatch = Regex.Match(evidence, @"^\d+");
        var count = countMatch.Success && int.TryParse(countMatch.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : 0;
        double? baseline = double.TryParse(Field("baseline-discard-rate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : null;

        var examples = new List<string>();
        var idx = Array.FindIndex(lines, l => l.Trim().Equals(ExamplesHeader, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            for (var i = idx + 1; i < lines.Length; i++)
            {
                var l = lines[i].Trim();
                if (!l.StartsWith("- ")) break;
                examples.Add(l[2..].Trim());
            }

        return new LearnedTrait(shortId!, rule, NeutralizeField(scope), status, date, count, NeutralizeField(evidence), NeutralizeField(Field("reasons")), baseline, examples);
    }

    public static string? RetireInFile(string fileText, string traitId, DateTime on)
    {
        var anchor = "^" + traitId;
        var anchorIdx = fileText.IndexOf(anchor, StringComparison.Ordinal);
        if (anchorIdx < 0) return null;
        var blockStart = fileText.LastIndexOf("\n## ", anchorIdx, StringComparison.Ordinal);
        blockStart = blockStart < 0 ? 0 : blockStart;
        var block = fileText[blockStart..anchorIdx];
        var replaced = Regex.Replace(block, @"status:\s*confirmed\s*\(\d{4}-\d{2}-\d{2}\)",
            "status: retired (" + on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")");
        if (replaced == block) return null;
        return fileText[..blockStart] + replaced + fileText[anchorIdx..];
    }
}
