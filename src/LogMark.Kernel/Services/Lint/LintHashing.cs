using System.Security.Cryptography;
using System.Text;

namespace LogMark.Services.Lint;

internal static class LintHashing
{
    /// <summary>
    /// Stable 16-char lowercase hex prefix of SHA256(input). Used for LintIssue
    /// DismissalStateHash so dismissed issues re-surface only when the underlying
    /// hashed state changes materially.
    /// </summary>
    public static string Compute(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
