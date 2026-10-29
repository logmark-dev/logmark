using System.Text;

namespace LogMark.Services;

/// <summary>
/// Enforces project house-style on free-form content written to the vault.
/// House-style rules: no em-dashes, no en-dashes, no smart quotes, no
/// horizontal-ellipsis character. ASCII-equivalents replace each.
/// Multi-byte text outside this set (Cyrillic, CJK, emoji) is preserved.
/// </summary>
public static class HouseStyleNormalizer
{
    public static string? Normalize(string? input)
    {
        if (input is null) return null;
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            switch (ch)
            {
                case '—': sb.Append("--"); break;      // em-dash
                case '–': sb.Append('-'); break;       // en-dash
                case '‘':
                case '’': sb.Append('\''); break;      // single curly
                case '“':
                case '”': sb.Append('"'); break;       // double curly
                case '…': sb.Append("..."); break;     // ellipsis
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
