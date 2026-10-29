namespace LogMark.Services.Search;

public static class LexicalScorer
{
    public static double Score(string text, ParsedQuery q)
    {
        if (q.Terms.Count == 0 || string.IsNullOrEmpty(text))
            return 0.0;

        var haystack = text.ToLowerInvariant();

        if (q.Terms.Count > 1 && q.Phrase != null && haystack.Contains(q.Phrase))
            return 3.0;

        return q.Terms.All(haystack.Contains) ? 2.0 : 0.0;
    }
}
