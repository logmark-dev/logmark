using LogMark.Services.EntryCache;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// EntryCache keyed files by their lowercased full path and handed that key
/// out as each entry's FilePath. Linux paths are case-sensitive, so for a
/// vault whose path has a capital letter the key named no file, and route
/// derivation (a case-sensitive Path.GetRelativePath there) put every entry
/// outside its route: route-scoped get_tasks/get_decisions/get_blocks came
/// back empty (rc7 osx QA follow-up, reproduced on Linux). CI temp paths are
/// lowercase, which is why nothing caught it.
/// </summary>
public sealed class EntryCacheVaultPathCaseTests : IDisposable
{
    private readonly string _vault = Path.Combine(Path.GetTempPath(), "LogMark-CasedVault-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_vault, true); } catch { }
    }

    [Fact]
    public void Entries_in_a_vault_whose_path_has_capitals_keep_their_route_and_a_real_file_path()
    {
        var system = Path.Combine(_vault, "logmark", "_system");
        Directory.CreateDirectory(system);
        File.WriteAllText(Path.Combine(system, "routes.md"), "# Routes\n\n## Projects\n\n### qa\n- path: qa\n- status: active\n");
        var tasks = Path.Combine(_vault, "logmark", "qa", "_tasks");
        Directory.CreateDirectory(tasks);
        var file = Path.Combine(tasks, "2026.md");
        File.WriteAllText(file, "## 2026-10-22 12:00\n\ncased task\n_task: none | postponed: 0 | c4a5ed01\n");

        using var cache = new EntryCache(_vault);
        cache.InvalidateFile(file);

        var entry = Assert.Single(cache.GetAllEntries());
        Assert.Equal("qa", entry.Route);
        Assert.True(File.Exists(entry.FilePath), entry.FilePath);
    }
}
