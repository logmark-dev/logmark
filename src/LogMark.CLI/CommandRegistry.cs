namespace LogMark.CLI;

/// <summary>
/// Registry for CLI commands. Provides lookup and "did you mean?" suggestions.
/// </summary>
public class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ICommand> Commands => _commands;

    public void Register(ICommand command)
    {
        _commands[command.Name] = command;
    }

    public ICommand? Find(string name)
    {
        return _commands.GetValueOrDefault(name);
    }

    /// <summary>
    /// Find similar command names for "did you mean?" suggestions.
    /// Returns commands with Levenshtein distance <= 3.
    /// </summary>
    public IReadOnlyList<string> FindSimilar(string name, int maxDistance = 3)
    {
        return _commands.Keys
            .Select(k => (Name: k, Distance: LevenshteinDistance(name.ToLowerInvariant(), k.ToLowerInvariant())))
            .Where(x => x.Distance <= maxDistance)
            .OrderBy(x => x.Distance)
            .Select(x => x.Name)
            .ToList();
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (s.Length == 0) return t.Length;
        if (t.Length == 0) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];

        for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;

        for (int i = 1; i <= s.Length; i++)
        {
            for (int j = 1; j <= t.Length; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[s.Length, t.Length];
    }
}
