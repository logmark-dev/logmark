using System.Text.RegularExpressions;
using LogMark.Models;

namespace LogMark.Services;

public class TagsFileParser
{
    private readonly string _tagsFilePath;

    private static readonly Regex ClusterHeader = new(@"^###\s+([\w-]+)\s*$", RegexOptions.Compiled);
    private static readonly Regex PropertyLine = new(@"^\s*-\s*(\w+):\s*(.*)$", RegexOptions.Compiled);

    public TagsFileParser(string tagsFilePath)
    {
        if (string.IsNullOrWhiteSpace(tagsFilePath))
            throw new ArgumentNullException(nameof(tagsFilePath));
        _tagsFilePath = tagsFilePath;
    }

    public IReadOnlyList<ManualCluster> Parse()
    {
        if (!File.Exists(_tagsFilePath))
            return Array.Empty<ManualCluster>();

        var content = File.ReadAllText(_tagsFilePath);
        return ParseContent(content);
    }

    public static IReadOnlyList<ManualCluster> ParseContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<ManualCluster>();

        var clusters = new List<ManualCluster>();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        string? currentName = null;
        List<string>? currentTags = null;
        string? currentContext = null;
        string? currentCanonical = null;

        void Flush()
        {
            if (currentName != null && currentTags != null && currentTags.Count > 0)
            {
                clusters.Add(new ManualCluster(
                    Name: currentName,
                    Tags: currentTags.ToArray(),
                    Context: currentContext,
                    CanonicalTag: currentCanonical
                ));
            }
            currentName = null;
            currentTags = null;
            currentContext = null;
            currentCanonical = null;
        }

        foreach (var line in lines)
        {
            var headerMatch = ClusterHeader.Match(line);
            if (headerMatch.Success)
            {
                Flush();
                currentName = headerMatch.Groups[1].Value;
                currentTags = new List<string>();
                continue;
            }

            if (currentName == null)
                continue;

            var propMatch = PropertyLine.Match(line);
            if (!propMatch.Success)
                continue;

            var key = propMatch.Groups[1].Value.ToLowerInvariant();
            var value = propMatch.Groups[2].Value.Trim();

            switch (key)
            {
                case "tags":
                    currentTags = value
                        .Split(',')
                        .Select(t => t.Trim().TrimStart('#'))
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                    break;
                case "context":
                    currentContext = value;
                    break;
                case "canonical":
                    currentCanonical = value.TrimStart('#');
                    break;
            }
        }

        Flush();
        return clusters;
    }
}
