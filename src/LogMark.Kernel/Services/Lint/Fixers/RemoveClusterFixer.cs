using System.Text.Json;
using LogMark.Services.EntryCache;

namespace LogMark.Services.Lint.Fixers;

/// <summary>
/// Removes a degenerate cluster block from tags.md. Unlike TagRewriteFixer (which
/// skips _system/ files), this fixer explicitly targets _system/tags.md, the only
/// file it ever writes. Pairs with DegenerateClusterLint.
/// </summary>
public class RemoveClusterFixer : ILintFixer
{
    public string Id => "remove-cluster";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private readonly IFileAccessor _fileAccessor;

    public RemoveClusterFixer(IFileAccessor fileAccessor)
        => _fileAccessor = fileAccessor ?? throw new ArgumentNullException(nameof(fileAccessor));

    public Task<LintFixResult> ApplyAsync(LintIssue issue, IVaultContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(issue.FixPayloadJson))
            return Task.FromResult(new LintFixResult(false, "FixPayloadJson is empty", null));

        Payload? payload;
        try { payload = JsonSerializer.Deserialize<Payload>(issue.FixPayloadJson, _json); }
        catch (JsonException ex)
        { return Task.FromResult(new LintFixResult(false, $"Malformed FixPayloadJson: {ex.Message}", null)); }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
            return Task.FromResult(new LintFixResult(false, "FixPayloadJson missing name", null));

        var tagsPath = Path.Combine(ctx.VaultPath, PathConstants.GetTagsPath());
        if (!_fileAccessor.Exists(tagsPath))
            return Task.FromResult(new LintFixResult(true, null, "no-op: tags.md not found"));

        var content = _fileAccessor.ReadAllText(tagsPath);
        var rewritten = TagsFileClusterEditor.RemoveCluster(content, payload.Name);

        if (string.Equals(rewritten, content, StringComparison.Ordinal))
            return Task.FromResult(new LintFixResult(true, null, $"no-op: cluster '{payload.Name}' not found"));

        _fileAccessor.WriteAllText(tagsPath, rewritten);
        return Task.FromResult(new LintFixResult(true, null, $"removed cluster '{payload.Name}'"));
    }

    private record Payload(string Name);
}
