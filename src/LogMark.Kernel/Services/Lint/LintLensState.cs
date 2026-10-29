using System.IO;
using System.Text.Json;

namespace LogMark.Services.Lint;

// Persists "last time the Lint lens was viewed" for the active vault. Used by
// the sidebar to compute the Lint-unread delta: count of open lint issues with
// FirstSeenAt > LastSeen. File: vault/logmark/_system/lint-lens.json
// Shape: { "lastSeenLint": "2026-05-26T09:00:00Z" }
public class LintLensState
{
    private readonly string _filePath;

    public LintLensState(string vaultRoot)
    {
        _filePath = Path.Combine(vaultRoot, "logmark", "_system", "lint-lens.json");
    }

    public DateTime LoadLastSeen()
    {
        if (!File.Exists(_filePath)) return DateTime.MinValue.ToUniversalTime();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_filePath));
            if (doc.RootElement.TryGetProperty("lastSeenLint", out var prop)
                && prop.ValueKind == JsonValueKind.String
                && DateTime.TryParse(prop.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch
        {
        }
        return DateTime.MinValue.ToUniversalTime();
    }

    public void SaveLastSeen(DateTime timestamp)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var payload = JsonSerializer.Serialize(new
        {
            lastSeenLint = timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        });
        File.WriteAllText(_filePath, payload);
    }
}
