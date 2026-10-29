using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Tools;
using LogMark.Services;
using Xunit;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

public class BackfillToolTests : IDisposable
{
    private readonly string _testVaultPath;
    private readonly BackfillEntryTool _backfillEntryTool;
    private readonly BackfillBatchTool _backfillBatchTool;

    public BackfillToolTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"logmark-mcp-backfill-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);

        // Create logmark system folder
        var logmarkDir = Path.Combine(_testVaultPath, PathConstants.GetSystemFolder());
        Directory.CreateDirectory(logmarkDir);

        // Create minimal routes file
        var routesPath = Path.Combine(_testVaultPath, PathConstants.GetRoutesPath());
        File.WriteAllText(routesPath, "# Routes\n\n## Projects\n\n### logmark\n- path: projects/logmark\n\n## Domains\n");

        var notationParser = new NotationParser();
        var vaultService = new VaultService(_testVaultPath, notationParser);
        var systemLogger = new SystemLogger(_testVaultPath);
        var routeService = new RouteService(_testVaultPath, systemLogger, new TestRoutePromptService());
        routeService.LoadRoutes();
        var sessionService = new SessionService(_testVaultPath, systemLogger);
        var sessionContext = new McpSessionContext(sessionService, systemLogger, "test-agent", "general");

        var vaultWriter = new VaultWriter();

        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            VaultWriter = vaultWriter,
            VaultService = vaultService,
            NotationParser = notationParser,
            RouteService = routeService,
            Logger = systemLogger,
            SessionService = sessionService,
            SessionContext = sessionContext
        };
        _backfillEntryTool = new BackfillEntryTool(services);
        _backfillBatchTool = new BackfillBatchTool(services);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, true);
    }

    #region BackfillEntryTool Tests

    [Fact]
    public async Task BackfillEntry_CreatesEntryWithHistoricalTimestamp()
    {
        // Arrange
        var args = CreateArgs(new
        {
            content = "d: Using WPF for Windows-first experience",
            timestamp = "2025-06-15T10:30:00Z",
            route = "+logmark",
            entry_type = "decision",
            tags = new[] { "frozen" }
        });

        // Act
        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Success", "true");
        json.Should().Contain("2025.md"); // Year file

        // Verify file content
        var filePath = Path.Combine(_testVaultPath, "logmark", "projects", "logmark", "_decisions", "2025.md");
        File.Exists(filePath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("## 2025-06-15 10:30");
        content.Should().Contain("Using WPF for Windows-first experience");
        content.Should().Contain("#frozen");
        content.Should().Contain("source: backfill");
    }

    [Fact]
    public async Task BackfillEntry_WithSource_SetsSourceAttribution()
    {
        // Arrange
        var args = CreateArgs(new
        {
            content = "Using SQLite for storage",
            timestamp = "2025-07-01T14:00:00Z",
            entry_type = "decision",
            source = "CLAUDE.md"
        });

        // Act
        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();

        // Unrouted entries go to _inbox
        var filePath = Path.Combine(_testVaultPath, "logmark", "_inbox", "_decisions", "2025.md");
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("source: backfill:CLAUDE.md");
        content.Should().Contain("Backfilled from CLAUDE.md");
    }

    [Fact]
    public async Task BackfillEntry_InsertsChronologically()
    {
        // Arrange - Create entries in non-chronological order
        var entry1 = CreateArgs(new
        {
            content = "Second entry",
            timestamp = "2025-06-15T14:00:00Z"
        });

        var entry2 = CreateArgs(new
        {
            content = "First entry",
            timestamp = "2025-06-15T10:00:00Z"
        });

        var entry3 = CreateArgs(new
        {
            content = "Third entry",
            timestamp = "2025-06-15T18:00:00Z"
        });

        // Act
        await _backfillEntryTool.ExecuteAsync(entry1, CancellationToken.None);
        await _backfillEntryTool.ExecuteAsync(entry2, CancellationToken.None);
        await _backfillEntryTool.ExecuteAsync(entry3, CancellationToken.None);

        // Assert - unrouted entries go to _inbox
        var filePath = Path.Combine(_testVaultPath, "logmark", "_inbox", "_diary", "2025.md");
        var content = await File.ReadAllTextAsync(filePath);

        // Check order: First, Second, Third
        var firstPos = content.IndexOf("First entry");
        var secondPos = content.IndexOf("Second entry");
        var thirdPos = content.IndexOf("Third entry");

        firstPos.Should().BeLessThan(secondPos);
        secondPos.Should().BeLessThan(thirdPos);
    }

    [Fact]
    public async Task BackfillEntry_MissingTimestamp_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new { content = "Test content" });

        // Act
        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("timestamp");
    }

    [Fact]
    public async Task BackfillEntry_InvalidTimestamp_ReturnsError()
    {
        // Arrange
        var args = CreateArgs(new
        {
            content = "Test content",
            timestamp = "not-a-date"
        });

        // Act
        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Invalid timestamp");
    }

    // Regression: the topic-vault skill and the older BackfillEntryTool schema
    // description told agents that `content` "may include" notation prefixes for
    // type. The parser does correctly detect the type (entries land in the
    // right folder), but StripNotation must remove the literal prefix from the
    // saved body -- the prefix is a typing signal, not body text.
    //
    // Observed leak: vaults/local-llm-track-2026-05/ shipped with `b:`/`d:`/`q:`/
    // `g:`/`a:`/`i:`/`t:` preserved as the first two characters of every entry
    // body. This test pins the contract for the full backfill-with-route path.
    [Theory]
    [InlineData("b:", "block",    "_blocks",     "hardware procurement is the throttle")]
    [InlineData("d:", "decision", "_decisions",  "dense for daily driving, MoE for batch")]
    [InlineData("q:", "question", "_questions",  "at what size does open-weight stop running locally")]
    [InlineData("g:", "gap",      "_gaps",       "no clear answer on hybrid attention under quant")]
    [InlineData("a:", "analogy",  "_analogies",  "this cycle resembles the Linux distro era")]
    [InlineData("i:", "idea",     "_ideas",      "every frontier release this cycle is MoE")]
    [InlineData("t:", "task",     "_tasks",      "benchmark Qwen 3.6-27B on my hardware")]
    public async Task BackfillEntry_StripsLeadingTypePrefixFromBody(
        string prefix, string entryType, string typeFolder, string body)
    {
        var args = CreateArgs(new
        {
            content = $"{prefix} {body}",
            timestamp = "2026-05-12T15:00:00Z",
            route = "+logmark",
            entry_type = entryType
        });

        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        var filePath = Path.Combine(
            _testVaultPath, "logmark", "projects", "logmark", typeFolder, "2026.md");
        File.Exists(filePath).Should().BeTrue($"{typeFolder}/2026.md should exist for {entryType}");

        var file = await File.ReadAllTextAsync(filePath);

        // Isolate the body line: skip the date header (which may carry a context
        // suffix like "| Backfill") and read the first non-empty line after it.
        var lines = file.Replace("\r\n", "\n").Split('\n');
        var headerIdx = Array.FindIndex(lines, l => l.StartsWith("## 2026-05-12 15:00"));
        headerIdx.Should().BeGreaterThanOrEqualTo(0, "header line should be present");
        var bodyLine = lines.Skip(headerIdx + 1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));

        bodyLine.Should().Be(body, $"prefix `{prefix}` leaked into saved body for {entryType}");
    }

    // Regression: a freshly backfilled entry's returned id MUST resolve back to
    // the on-disk entry, so that write_page / organize_entry / read_entry can
    // act on it within the same session. The bug: BackfillEntryTool persisted no
    // _id: anchor and returned a content-hash id that nothing on disk reproduces,
    // so EntryPageLinker.FindEntryByShortId always threw EntryNotFoundException.
    [Fact]
    public async Task BackfillEntry_ReturnedId_PersistsAndResolvesForPageLinking()
    {
        var args = CreateArgs(new
        {
            content = "Karpathy on the LLM operating system",
            timestamp = "2025-06-15T10:30:00Z",
            route = "+logmark",
            entry_type = "idea"
        });

        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        var entryId = JsonDocument.Parse(result.Content[0].Text!)
            .RootElement.GetProperty("EntryId").GetString();
        entryId.Should().NotBeNullOrEmpty();

        // The returned id is persisted on disk as an _id: anchor.
        var filePath = Path.Combine(_testVaultPath, "logmark", "projects", "logmark", "_ideas", "2025.md");
        var fileContent = await File.ReadAllTextAsync(filePath);
        fileContent.Should().Contain($"_id: {entryId}");

        // And it resolves through the page-linking path (the reported failure).
        var subpageWriter = new SubpageWriter(_testVaultPath, new VaultWriter());
        var linker = new EntryPageLinker(_testVaultPath, subpageWriter, new VaultWriter());
        var link = () => linker.WritePageAndLink(
            route: "logmark", typeName: "ideas",
            filename: "2025-06-15-karpathy-llm-os.md",
            content: "# Karpathy\n\nNotes.", entryShortId: entryId!, displayText: "Karpathy");
        link.Should().NotThrow<EntryNotFoundException>();
    }

    [Fact]
    public async Task BackfillBatch_ReturnedId_PersistsAsIdAnchor()
    {
        var args = CreateArgs(new
        {
            entries = new[]
            {
                new
                {
                    content = "Using WPF",
                    timestamp = "2025-06-01T10:00:00Z",
                    route = "+logmark",
                    entry_type = "decision"
                }
            }
        });

        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        var entryId = JsonDocument.Parse(result.Content[0].Text!)
            .RootElement.GetProperty("Entries")[0].GetProperty("EntryId").GetString();
        entryId.Should().NotBeNullOrEmpty();

        var filePath = Path.Combine(_testVaultPath, "logmark", "projects", "logmark", "_decisions", "2025.md");
        var fileContent = await File.ReadAllTextAsync(filePath);
        fileContent.Should().Contain($"_id: {entryId}");
    }

    [Fact]
    public async Task BackfillEntry_TaskType_PersistsShortIdInTaskMetadata()
    {
        var args = CreateArgs(new
        {
            content = "benchmark Qwen on local hardware",
            timestamp = "2025-06-20T09:00:00Z",
            route = "+logmark",
            entry_type = "task"
        });

        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);
        result.IsError.Should().BeFalse(because: result.Content[0].Text);

        var entryId = JsonDocument.Parse(result.Content[0].Text!)
            .RootElement.GetProperty("EntryId").GetString();
        entryId.Should().NotBeNullOrEmpty();

        var filePath = Path.Combine(_testVaultPath, "logmark", "projects", "logmark", "_tasks", "2025.md");
        var fileContent = await File.ReadAllTextAsync(filePath);
        // Tasks carry their ShortId on the _task: line, not an _id: line.
        fileContent.Should().Contain($"| {entryId}");
        fileContent.Should().Contain("_task:");
    }

    [Fact]
    public async Task BackfillEntry_WithScope_CreatesTwoLevelPath()
    {
        // Arrange
        var args = CreateArgs(new
        {
            content = "d: Debug hotkey is Ctrl+Backspace",
            timestamp = "2025-06-01T09:00:00Z",
            route = "+logmark",
            scope = "ux",
            entry_type = "decision",
            tags = new[] { "frozen" }
        });

        // Act
        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        // Two-level routing: projects/logmark/ux
        json.Should().Contain("projects/logmark/ux");
    }

    #endregion

    // A +word quoted in prose ("ASX +Security Description") is text, not a
    // route: it must survive the write (lmk-headless-inline-route-tokens-cli-backfill-b184).
    [Fact]
    public async Task BackfillEntry_KeepsProsePlusWordsVerbatim()
    {
        var args = CreateArgs(new
        {
            content = "the printed 'ASX +Security Description' field decides",
            timestamp = "2025-06-15T10:30:00Z",
            route = "+logmark"
        });

        var result = await _backfillEntryTool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var file = Path.Combine(_testVaultPath, PathConstants.GetRoutedFilePath("projects/logmark", "diary", 2025));
        File.ReadAllText(file).Should().Contain("'ASX +Security Description'");
    }

    [Fact]
    public async Task BackfillBatch_KeepsProsePlusWordsVerbatim()
    {
        var args = CreateArgs(new
        {
            entries = new[]
            {
                new { content = "priced at +USD 5,999 street", timestamp = "2025-06-16T10:30:00Z", route = "+logmark" }
            }
        });

        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        result.IsError.Should().BeFalse(result.Content[0].Text);
        var file = Path.Combine(_testVaultPath, PathConstants.GetRoutedFilePath("projects/logmark", "diary", 2025));
        File.ReadAllText(file).Should().Contain("priced at +USD 5,999 street");
    }

    #region BackfillBatchTool Tests

    [Fact]
    public async Task BackfillBatch_CreatesMultipleEntries()
    {
        // Arrange
        var args = CreateArgs(new
        {
            entries = new[]
            {
                new
                {
                    content = "d: Using WPF",
                    timestamp = "2025-06-01T10:00:00Z",
                    entry_type = "decision",
                    tags = new[] { "frozen" }
                },
                new
                {
                    content = "d: Using SQLite",
                    timestamp = "2025-06-15T14:00:00Z",
                    entry_type = "decision",
                    tags = new[] { "frozen" }
                }
            }
        });

        // Act
        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Success", "true");
        json.ShouldHaveJsonProperty("Created", "2");
        json.ShouldHaveJsonProperty("Failed", "0");
    }

    [Fact]
    public async Task BackfillBatch_DryRun_DoesNotWriteFiles()
    {
        // Arrange
        var args = CreateArgs(new
        {
            entries = new[]
            {
                new
                {
                    content = "Test entry",
                    timestamp = "2025-06-01T10:00:00Z"
                }
            },
            dry_run = true
        });

        // Act
        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("DryRun", "true");
        json.ShouldHaveJsonProperty("Created", "1");

        // Verify no file was created
        var filePath = Path.Combine(_testVaultPath, "logmark", "_diary", "2025.md");
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task BackfillBatch_StopOnError_StopsAtFirstFailure()
    {
        // Arrange
        var args = CreateArgs(new
        {
            entries = new object[]
            {
                new { content = "Valid entry", timestamp = "2025-06-01T10:00:00Z" },
                new { content = "Missing timestamp" }, // Will fail
                new { content = "Should not process", timestamp = "2025-06-02T10:00:00Z" }
            },
            stop_on_error = true
        });

        // Act
        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Created", "1");
        json.ShouldHaveJsonProperty("Failed", "1");
        json.ShouldHaveJsonProperty("Total", "2"); // Stopped at second, didn't process third
    }

    [Fact]
    public async Task BackfillBatch_ContinueOnError_ProcessesAllEntries()
    {
        // Arrange
        var args = CreateArgs(new
        {
            entries = new object[]
            {
                new { content = "Valid entry 1", timestamp = "2025-06-01T10:00:00Z" },
                new { content = "Missing timestamp" }, // Will fail
                new { content = "Valid entry 2", timestamp = "2025-06-02T10:00:00Z" }
            },
            stop_on_error = false
        });

        // Act
        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Created", "2");
        json.ShouldHaveJsonProperty("Failed", "1");
        json.ShouldHaveJsonProperty("Total", "3");
    }

    [Fact]
    public async Task BackfillBatch_MaintainsChronologicalOrder()
    {
        // Arrange - Send entries in non-chronological order
        var args = CreateArgs(new
        {
            entries = new[]
            {
                new { content = "Second entry", timestamp = "2025-06-15T14:00:00Z" },
                new { content = "First entry", timestamp = "2025-06-15T10:00:00Z" },
                new { content = "Third entry", timestamp = "2025-06-15T18:00:00Z" }
            }
        });

        // Act
        await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert - unrouted entries go to _inbox
        var filePath = Path.Combine(_testVaultPath, "logmark", "_inbox", "_diary", "2025.md");
        var content = await File.ReadAllTextAsync(filePath);

        var firstPos = content.IndexOf("First entry");
        var secondPos = content.IndexOf("Second entry");
        var thirdPos = content.IndexOf("Third entry");

        firstPos.Should().BeLessThan(secondPos);
        secondPos.Should().BeLessThan(thirdPos);
    }

    [Fact]
    public async Task BackfillBatch_EmptyArray_ReturnsSuccess()
    {
        // Arrange
        var args = CreateArgs(new { entries = Array.Empty<object>() });

        // Act
        var result = await _backfillBatchTool.ExecuteAsync(args, CancellationToken.None);

        // Assert
        result.IsError.Should().BeFalse();
        var json = result.Content[0].Text;
        json.ShouldHaveJsonProperty("Success", "true");
        json.ShouldHaveJsonProperty("Total", "0");
    }

    #endregion

    private static Dictionary<string, object?> CreateArgs(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, object?>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = prop.Value.Clone();
        }

        return result;
    }

    private class TestRoutePromptService : IRoutePromptService
    {
        public Task<RoutePromptResult> PromptForRouteCreationAsync(string routeName, RoutePromptOptions options)
        {
            var path = options.DefaultPath ?? $"projects/{routeName}";
            return Task.FromResult(new RoutePromptResult(RoutePromptOutcome.Create, path));
        }
    }
}
