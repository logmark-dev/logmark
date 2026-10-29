namespace LogMark.Services.Search;

public static class QueryParser
{
    public static ParsedQuery Parse(string raw)
    {
        var terms = new List<string>();
        var tags = new List<string>();
        var routes = new List<string>();

        var tokens = (raw ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var t = token.ToLowerInvariant();
            if (t.StartsWith('#') && t.Length > 1)
                tags.Add(t[1..]);
            else if (t.StartsWith('+') && t.Length > 1)
                routes.Add(t[1..]);
            else
                terms.Add(t);
        }

        var phrase = terms.Count > 0 ? string.Join(' ', terms) : null;
        return new ParsedQuery(terms, phrase, tags, routes);
    }
}
