using System.Text.Json;

namespace LogMark.Services.Lint.Persistence;

public class JsonLintStateStore : ILintStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    public JsonLintStateStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public async Task<LintState> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return LintState.Empty;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<LintState>(stream, JsonOptions, ct);
            // Version mismatch: drop v1 state from Sprint 2; user re-scans to rebuild under new rule IDs.
            if (state == null || state.Version != LintState.CurrentVersion)
                return LintState.Empty;
            return state;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // Corrupt JSON or transient file lock (antivirus, sync client);
            // fall back to empty state.
            return LintState.Empty;
        }
    }

    public async Task SaveAsync(LintState state, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var tempPath = _path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
            await stream.FlushAsync(ct);
        }

        // File.Move with overwrite=true is atomic on Windows when source and dest
        // are on the same volume.
        File.Move(tempPath, _path, overwrite: true);
    }
}
