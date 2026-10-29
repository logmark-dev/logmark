namespace LogMark.Services.Providers;

/// <summary>
/// SemVer 2.0 precedence for provider BinVersion strings ("0.23.0",
/// "0.1.0-rc6"). Build metadata after '+' is ignored. A release outranks its
/// prereleases; numeric prerelease identifiers compare numerically, others
/// ordinally (so "rc10" sorts before "rc6", exactly as SemVer specifies).
/// Unparseable input ranks below every parseable version.
/// </summary>
public static class SemVer
{
    public static IComparer<string?> Comparer { get; } = Comparer<string?>.Create(Compare);

    public static int Compare(string? a, string? b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        if (pa is null && pb is null) return 0;
        if (pa is null) return -1;
        if (pb is null) return 1;

        var core = pa.Value.Core.CompareTo(pb.Value.Core);
        return core != 0 ? core : ComparePrerelease(pa.Value.Pre, pb.Value.Pre);
    }

    private static (Version Core, string[] Pre)? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = LogMark.BuildInfo.StripMetadata(value.Trim());
        var dash = s.IndexOf('-');
        var corePart = dash >= 0 ? s[..dash] : s;
        if (!Version.TryParse(corePart, out var core)) return null;
        var pre = dash >= 0 ? s[(dash + 1)..].Split('.') : Array.Empty<string>();
        return (core, pre);
    }

    private static int ComparePrerelease(string[] a, string[] b)
    {
        if (a.Length == 0 && b.Length == 0) return 0;
        if (a.Length == 0) return 1;
        if (b.Length == 0) return -1;

        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNum = int.TryParse(a[i], out var ai);
            var bNum = int.TryParse(b[i], out var bi);
            var c = (aNum, bNum) switch
            {
                (true, true) => ai.CompareTo(bi),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i])
            };
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }
}
