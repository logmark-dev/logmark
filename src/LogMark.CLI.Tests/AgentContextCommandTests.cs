using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using LogMark.CLI.Commands;
using LogMark.TestUtilities;
using Xunit;

namespace LogMark.CLI.Tests;

[Collection("EnvironmentTests")]
public class AgentContextCommandTests : IDisposable
{
    private readonly TestVaultBuilder _vault;

    public AgentContextCommandTests()
    {
        _vault = new TestVaultBuilder().WithStandardStructure();
    }

    public void Dispose() => _vault.Dispose();

    private const string SessionStartPayload =
        """{"session_id":"s1","transcript_path":"x.jsonl","cwd":"/p","permission_mode":"default","hook_event_name":"SessionStart"}""";

    [Fact]
    public void WithVaultContent_EmitsHookJson_WithEntriesAndIds()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: stuck on auth flow");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "q: which db do we use");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "d: ship friday");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"hookSpecificOutput\"");
        result.StdOut.Should().Contain("\"hookEventName\":\"SessionStart\"");
        result.StdOut.Should().Contain("additionalContext");
        result.StdOut.Should().Contain("stuck on auth flow");
        result.StdOut.Should().Contain("which db do we use");
        result.StdOut.Should().Contain("ship friday");
        result.StdOut.Should().Contain("(id: ");
        result.StdOut.Should().Contain("read_entry");
    }

    [Fact]
    public void OpenTasks_AreInjected()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: wire the export pipeline");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Open tasks");
        result.StdOut.Should().Contain("wire the export pipeline");
    }

    [Fact]
    public void RecentLogs_AreInjected_AsRecentActivity()
    {
        // A no-prefix capture is a Log entry (the session handoff / quick note).
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "left off mid-refactor of the router");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Recent activity");
        result.StdOut.Should().Contain("left off mid-refactor");
    }

    [Fact]
    public void ThinScope_EnrichesWithRecentIdeas()
    {
        // Only an idea -> primary state is empty (thin) -> enrich with ideas.
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "i: maybe a plugin marketplace");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Recent ideas");
        result.StdOut.Should().Contain("maybe a plugin marketplace");
    }

    [Fact]
    public void SubstantivePrimary_OmitsIdeaBacklog()
    {
        // >= EnrichmentThreshold primary entries -> the idea backlog stays out.
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: block one");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: block two");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: block three");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "i: speculative idea xyz");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("block one");
        result.StdOut.Should().NotContain("Recent ideas");
        result.StdOut.Should().NotContain("speculative idea xyz");
    }

    [Fact]
    public void EmptyVault_EmitsNothing_ExitsZero()
    {
        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Trim().Should().BeEmpty();
    }

    [Fact]
    public void NoVault_ExitsZero_EmitsNothing()
    {
        var result = CliRunner.RunWithStdin(SessionStartPayload,
            "--vault", Path.Combine(Path.GetTempPath(), "lm-missing-" + Guid.NewGuid().ToString("N")[..8]),
            "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Trim().Should().BeEmpty();
    }

    [Fact]
    public void MalformedStdin_StillWorks()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: a real block");

        var result = CliRunner.RunWithStdin("not json at all", "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("a real block");
    }

    [Fact]
    public void ManyEntries_OutputCapped()
    {
        for (var i = 0; i < 40; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"b: block number {i} with a reasonably long line of text to fill space");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Length.Should().BeLessThan(4500, "one populated section is well within the per-section budget");
    }

    [Fact]
    public void RouteFilter_LimitsToRoute()
    {
        // Capture routes only to declared routes; an inline +word creates nothing.
        WriteRoutes("backend", "frontend");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: backend block +backend");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: frontend block +frontend");

        var result = CliRunner.RunWithStdin(SessionStartPayload,
            "--vault", _vault.VaultPath, "agent-context", "--route", "backend");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("backend block");
        result.StdOut.Should().NotContain("frontend block");
    }

    [Fact]
    public void ContextCap_FooterSurvivesTruncation()
    {
        // Three sections x 30 entries each with ~120-char lines; each type is queried
        // with Limit=MaxPerSection=10, so at most 10 land per section - the footer
        // (read_entry line) must survive regardless of how the sections render.
        //
        // The previous version of this test asserted the whole output stayed under
        // 2000 chars, which pinned exactly the defect the final review named: the
        // Task-6 reserve literals summed to the old 3000 default, so they - not the
        // profile's 8000 - were the ceiling, and this fixture rendered about 9 of its
        // 30 fetched items. It is replaced rather than relaxed: the reserves are now
        // weights scaled to ContextBudgetChars, so all 30 fit and the assertion is
        // that they do. Reverting the scaling drops the item count back to single
        // digits and fails the first assertion, so this is a discriminating bound and
        // not a raised ceiling.
        var longLine = new string('x', 100);
        for (var i = 0; i < 30; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"b: block {i} {longLine}");
        for (var i = 0; i < 30; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"q: question {i} {longLine}");
        for (var i = 0; i < 30; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"d: decision {i} {longLine}");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("read_entry", "footer must survive context-cap truncation");

        using var doc = JsonDocument.Parse(result.StdOut);
        var additionalContext = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;

        additionalContext.Split('\n').Count(l => l.StartsWith("- ")).Should().Be(30,
            "10 items per section x 3 populated sections all fit once the shares scale to the host budget");
        additionalContext.Should().NotContain("more)",
            "nothing was dropped, so no section reports a dropped count");

        // Claude Code's ContextBudgetChars is 8000; the envelope adds JSON
        // escaping on top. This is the profile budget doing the bounding now.
        result.StdOut.Length.Should().BeLessThan(9000, "total output stays within the host's context budget");
    }

    [Fact]
    public void Framework_Gemini_OmitsHookEventName()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: a gemini block");

        var result = CliRunner.RunWithStdin(SessionStartPayload,
            "--vault", _vault.VaultPath, "agent-context", "--framework", "gemini-cli");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("hookSpecificOutput").And.Contain("additionalContext");
        result.StdOut.Should().NotContain("hookEventName", "Gemini's wrapper carries no hookEventName");
        result.StdOut.Should().Contain("a gemini block");
    }

    [Fact]
    public void Framework_Cursor_UsesSnakeCaseAdditionalContext()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: a cursor block");

        var result = CliRunner.RunWithStdin(SessionStartPayload,
            "--vault", _vault.VaultPath, "agent-context", "--framework", "cursor");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("additional_context");
        result.StdOut.Should().NotContain("hookSpecificOutput");
        result.StdOut.Should().Contain("a cursor block");
    }

    [Fact]
    public void Framework_UnknownOrAbsent_FallsBackToClaudeShape()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: a fallback block");

        var result = CliRunner.RunWithStdin(SessionStartPayload,
            "--vault", _vault.VaultPath, "agent-context", "--framework", "no-such-agent");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("\"hookEventName\":\"SessionStart\"");
        result.StdOut.Should().Contain("a fallback block");
    }

    private string RoutesFilePath() =>
        new LogMark.Services.RouteParser(_vault.VaultPath).GetRoutesFilePath();

    private void WriteRoutesWithWorkdir(string route, string workdir, params string[] extraRoutes)
    {
        var path = RoutesFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            # Routes

            ## Projects

            ### {route}
            - path: projects/{route}
            - workdir: {workdir}

            """ + DeclaredRoutes(extraRoutes));
    }

    private void WriteRoutes(params string[] routes)
    {
        var path = RoutesFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "# Routes\n\n## Projects\n" + DeclaredRoutes(routes));
    }

    private static string DeclaredRoutes(IEnumerable<string> routes) =>
        string.Concat(routes.Select(r => $"\n### {r}\n- path: projects/{r}\n"));

    private static string PayloadWithCwd(string cwd) =>
        $$"""{"session_id":"s1","cwd":{{System.Text.Json.JsonSerializer.Serialize(cwd)}},"hook_event_name":"SessionStart"}""";

    [Fact]
    public void Cwd_BoundToRoute_ScopesToThatRoute()
    {
        var workdir = Path.Combine(Path.GetTempPath(), "lm-wd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workdir);
        WriteRoutesWithWorkdir("photomind", workdir, "logmark");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: photomind block +photomind");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: logmark block +logmark");

        var result = CliRunner.RunWithStdin(PayloadWithCwd(workdir),
            "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("photomind block");
        result.StdOut.Should().NotContain("logmark block");
    }

    [Fact]
    public void Cwd_Unbound_WithWorkdirsPresent_EmitsLinkHint()
    {
        var bound = Path.Combine(Path.GetTempPath(), "lm-wd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(bound);
        WriteRoutesWithWorkdir("photomind", bound);
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: some block +photomind");

        var elsewhere = Path.Combine(Path.GetTempPath(), "lm-elsewhere-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(elsewhere);
        var result = CliRunner.RunWithStdin(PayloadWithCwd(elsewhere),
            "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("lm link");
        result.StdOut.Should().NotContain("some block");
    }

    [Fact]
    public void Cwd_NoWorkdirsAnywhere_FallsBackToWholeVault()
    {
        // routes.md has no workdir at all -> feature unused -> today's behavior
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: a plain block");

        var result = CliRunner.RunWithStdin(PayloadWithCwd(@"C:\anywhere"),
            "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("a plain block");
    }

    [Fact]
    public void Compact_Source_PrependsRegroundingLine()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: wire the compact path");

        const string compactPayload =
            """{"session_id":"s1","transcript_path":"x.jsonl","cwd":"/p","permission_mode":"default","hook_event_name":"SessionStart","source":"compact"}""";

        var result = CliRunner.RunWithStdin(compactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(result.StdOut);
        var additionalContext = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;

        // Task 6 replaced the one-line RegroundingLine prepend this test used to
        // pin with the continuation shape: a "Continuing work..." lead line that
        // carries the same RegroundingLine wording as its second sentence, so the
        // re-grounding nudge survives (now mid-lead rather than as the whole
        // payload's first sentence) alongside the handoff-led body it introduces.
        additionalContext.Should().StartWith("Continuing work",
            "the compact shape leads with the continuation framing");
        additionalContext.Should().Contain(AgentContextCommand.RegroundingLine,
            "the re-grounding nudge must still reach the agent");
        additionalContext.Should().Contain("wire the compact path",
            "vault context must still appear after the nudge");
    }

    [Fact]
    public void Startup_Source_NoRegroundingLine()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: a task for startup test");

        const string startupPayload =
            """{"session_id":"s1","transcript_path":"x.jsonl","cwd":"/p","permission_mode":"default","hook_event_name":"SessionStart","source":"startup"}""";

        var result = CliRunner.RunWithStdin(startupPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().NotContain(AgentContextCommand.RegroundingLine,
            "non-compact source must not inject the re-grounding nudge");
    }

    [Fact]
    public void BakedRoute_OverridesCwdResolution()
    {
        var workdir = Path.Combine(Path.GetTempPath(), "lm-wd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workdir);
        WriteRoutesWithWorkdir("photomind", workdir, "logmark");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: photomind block +photomind");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: logmark block +logmark");

        // cwd would resolve to photomind, but --route logmark pins it
        var result = CliRunner.RunWithStdin(PayloadWithCwd(workdir),
            "--vault", _vault.VaultPath, "agent-context", "--route", "logmark");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("logmark block");
        result.StdOut.Should().NotContain("photomind block");
    }

    private const string CompactPayload =
        """{"session_id":"s1","transcript_path":"x.jsonl","cwd":"/p","source":"compact","hook_event_name":"SessionStart"}""";

    /// <summary>
    /// Writes an entry directly to a type folder's year file, bypassing capture
    /// so the timestamp can be backdated (capture always stamps "now"). Uses
    /// the same metadata-line conventions SearchLifecycleTests /
    /// DismissalVisibilityTests use (the "_task: ..." / "_id: ..." trailer,
    /// "#dismissed" on its own line) - but this is NOT their verbatim on-disk
    /// form: it inserts a blank line between the "## " header and the content
    /// line that neither FileRouter.AppendEntry nor those fixtures produce.
    /// Harmless here (EntryParser skips blank lines unconditionally, so
    /// parsing is unaffected), but a caller after byte-exact fidelity to a
    /// real capture should not rely on this helper for it.
    /// </summary>
    private void SeedRawEntry(string typeFolder, DateTime timestamp, string content, bool isTask, bool dismissed = false)
    {
        var path = Path.Combine(_vault.LogMarkPath, typeFolder, $"{timestamp.Year}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shortId = Guid.NewGuid().ToString("N")[..8];
        var metaLine = isTask
            ? $"_task: none | postponed: 0 | {shortId} ^{shortId}"
            : $"_id: {shortId} ^{shortId}";
        var tagBlock = dismissed ? "\n\n#dismissed" : "";
        var block = $"## {timestamp:yyyy-MM-dd HH:mm}\n\n{content}\n{metaLine}{tagBlock}\n";

        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var full = string.IsNullOrWhiteSpace(existing)
            ? block
            : existing.TrimEnd('\n') + "\n\n" + block;
        File.WriteAllText(path, full);
    }

    [Fact]
    public void OnCompact_LeadsWithTheHandoff()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "stopped mid-refactor of the router #handoff");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: an old blocker nobody is working on");

        var result = CliRunner.RunWithStdin(CompactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        result.StdOut.Should().Contain("Where you left off");
        var handoffAt = result.StdOut.IndexOf("stopped mid-refactor", StringComparison.Ordinal);
        handoffAt.Should().BeGreaterThan(-1);
        result.StdOut.Should().Contain("Continuing work");
    }

    [Fact]
    public void OnCompact_ReplacesTheStaleBacklogWithACountAndARetrievalHint()
    {
        // The brief's original version captured the block seconds before
        // asserting, so it landed inside the 24h in-flight window and nothing
        // was left "other" - the assertion could never pass. Seed a genuinely
        // OUT-OF-WINDOW open block (task-6 decision 2) so there is something
        // the payload deliberately does not show inline.
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "stopped mid-refactor #handoff");
        SeedRawEntry("_blocks", DateTime.Now.AddDays(-30), "an old blocker nobody is working on", isTask: false);

        var result = CliRunner.RunWithStdin(CompactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.StdOut.Should().NotContain("Open blocks");
        result.StdOut.Should().Contain("other open items");
        result.StdOut.Should().Contain("get_project_context");
    }

    [Fact]
    public void OnFreshStart_TheBacklogStillRenders()
    {
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: an old blocker nobody is working on");

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.StdOut.Should().Contain("Open blocks");
        result.StdOut.Should().Contain("an old blocker");
    }

    [Fact]
    public void DismissedWorkNeverReachesThePayload()
    {
        // The defect lmk-nnd5 recorded: a dismissed item in the briefing caused
        // a groom run to be requested for work already closed.
        //
        // The brief's dismissal test calls a CLI verb ("reflect dismiss_task")
        // that does not exist - dismiss_task is a verb registered on the Reflect
        // BuiltinVerbs group, not a command-line surface (task-6 decision 1).
        // Seed the dismissed task by writing the vault markdown directly, in the
        // verbatim on-disk form DismissalVisibilityTests/SearchLifecycleTests use:
        // the #dismissed tag on its own line after the metadata trailer.
        //
        // An open task is seeded alongside it (task-6 advisor note) so a GREEN
        // run proves the payload still renders open work and excludes only the
        // dismissed one - without it, GREEN would also happen to pass on an
        // empty/vacuous payload.
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "t: an open task that stays visible");
        SeedRawEntry("_tasks", DateTime.Now, "a task that was later dismissed", isTask: true, dismissed: true);

        var result = CliRunner.RunWithStdin(SessionStartPayload, "--vault", _vault.VaultPath, "agent-context");

        result.StdOut.Should().Contain("an open task that stays visible");
        result.StdOut.Should().NotContain("a task that was later dismissed");
    }

    [Fact]
    public void OnCompact_InFlightSection_TruncatesAtAnItemBoundaryUnderBudget()
    {
        // Pathological in-flight count: more open blocks/tasks captured within
        // the 24h window than the "In flight" section's fixed reserve (1200
        // chars) can hold, even capped at MaxPerSection=10 each. This is the
        // scenario a whole-payload character cap would hard-slice mid-item;
        // routing through AgentContextPayload.Render (Task 5) instead must
        // stop at an item boundary and report how many it dropped.
        var longLine = new string('x', 100);
        for (var i = 0; i < 10; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"b: block {i} {longLine}");
        for (var i = 0; i < 10; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"t: task {i} {longLine}");

        var result = CliRunner.RunWithStdin(CompactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(result.StdOut);
        var additionalContext = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;

        // Stays within the profile's overall budget (claude-code default is
        // 8000; this fixture's 20 candidate items would run well past that if
        // rendered unbounded, so a comfortably tighter bound still proves the
        // reserve - not the whole-payload cap - is what constrained this).
        additionalContext.Length.Should().BeLessThan(3000,
            "the In flight section's own reserve, not the whole-payload cap, should bound this");

        // The dropped-count note only exists on the Render code path - a raw
        // string.Join of every in-flight item would never emit it.
        additionalContext.Should().MatchRegex(@"\(\+\d+ more\)",
            "Render must report how many in-flight items it could not fit");

        // Every rendered item line is whole, not cut mid-sentence: each one
        // still ends with its intact "(id: xxxxxxxx)" suffix. A naive
        // character-slice truncation could leave a final line with content
        // but no id, or a partial id.
        var itemLines = additionalContext.Split('\n').Where(l => l.StartsWith("- ")).ToList();
        itemLines.Should().NotBeEmpty();
        foreach (var line in itemLines)
            line.Should().MatchRegex(@"\(id: [0-9a-f]{8}\)$",
                $"line must be a whole, uncut item: \"{line}\"");
    }

    [Fact]
    public void OnCompact_PointerLine_CountsItemsRenderDropped_NotJustCandidates()
    {
        // Compound case: more open blocks than the query limit (15 open,
        // MaxPerSection = 10 fetched) AND more of the fetched, in-window
        // candidates than the "In flight" section's own reserve can render.
        // The pointer's arithmetic must count BOTH cuts - the query-limit cut
        // down to 10, and the render-budget cut down further - not just the
        // first. That is the exact undercount the finding named: 15 open, 10
        // fetched, fewer than 10 actually rendered; "other" must be 15 minus
        // what was actually shown, not 15 minus what was merely fetched.
        //
        // No #handoff entry is captured here: HandoffLine also emits a "- "
        // prefixed line, and this test counts "- " lines to determine exactly
        // how many in-flight items rendered, so a handoff would contaminate
        // that count.
        var longLine = new string('x', 100);
        for (var i = 0; i < 15; i++)
            CliRunner.Run("--vault", _vault.VaultPath, "capture", $"b: block {i} {longLine}");

        var result = CliRunner.RunWithStdin(CompactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(result.StdOut);
        var additionalContext = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;

        var actuallyShown = additionalContext.Split('\n').Count(l => l.StartsWith("- "));
        // Proves the compound case actually happened: 10 were fetched (the
        // query limit), and fewer than that were rendered (the reserve cut).
        // If this ever equals 10, the reserve wasn't tight enough for this
        // fixture and the test below would only be exercising the query-limit
        // cut, not the compound case it's named for.
        actuallyShown.Should().BeLessThan(10,
            "the reserve must drop at least one fetched item for this to be the compound case");

        var match = Regex.Match(additionalContext, @"\((\d+) tasks, (\d+) blocks\)");
        match.Success.Should().BeTrue("the pointer line must appear");
        var reportedOtherBlocks = int.Parse(match.Groups[2].Value);

        reportedOtherBlocks.Should().Be(15 - actuallyShown,
            "the pointer must count everything the payload did not show, including what Render dropped for " +
            "budget reasons on top of the query-limit cut - not just items outside the fetched, in-window page");
    }

    // PointerLine is exercised directly rather than through the CLI for the
    // review-count clause: CliContext.PendingDistillReviewCount returns null on
    // the open build by design (the queue lives in LogMark.Core, across the
    // boundary OpenBuildContractTests guards), so no open-harness run can ever
    // reach a non-null count.

    [Fact]
    public void PointerLine_WithOnlyPendingReviews_HasNoLeadingSpace()
    {
        // The space belonged to the second clause, so with nothing to say about
        // open items the line rendered as "\n 3 distilled...".
        var line = AgentContextCommand.PointerLine(otherTasks: 0, otherBlocks: 0, pendingReviews: 3);

        line.Should().StartWith("\n");
        line.Should().NotStartWith("\n ");
    }

    [Fact]
    public void PointerLine_WithNothingLeftOver_StillCarriesARetrievalInstruction()
    {
        // The continuation payload used to end with no retrieval instruction at
        // all when nothing was left over - the fresh shape's read_entry footer
        // does not exist on that path.
        var line = AgentContextCommand.PointerLine(otherTasks: 0, otherBlocks: 0, pendingReviews: null);

        line.Should().Contain("read_entry");
    }

    [Fact]
    public void PointerLine_ReviewClause_SaysWhereReviewHappens()
    {
        // The count is surfaced deliberately, but it named an action the agent
        // cannot take: no MCP tool exposes the session queue. Saying where
        // review happens stops an agent hunting for a tool that does not exist.
        var line = AgentContextCommand.PointerLine(otherTasks: 2, otherBlocks: 1, pendingReviews: 3);

        line.Should().Contain("3 distilled sessions await review");
        line.Should().Contain("desktop feed");
    }

    [Fact]
    public void PointerLine_ReviewClause_ReadsCorrectlyForASingleSession()
    {
        // The clause is a sentence an agent reads, not a field it parses, so
        // "1 distilled sessions await review" would read as machine output.
        var line = AgentContextCommand.PointerLine(otherTasks: 0, otherBlocks: 0, pendingReviews: 1);

        line.Should().Contain("1 distilled session awaits review in the LogMark desktop feed.");
    }

    [Fact]
    public void OnCompact_WithNothingLeftOver_TheOutputStillSaysHowToFetchDetail()
    {
        // End-to-end counterpart to the PointerLine unit above: everything open
        // is in-window and rendered, so there is no "other open items" clause -
        // and the payload must still tell the agent how to reach detail.
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "stopped mid-refactor #handoff");
        CliRunner.Run("--vault", _vault.VaultPath, "capture", "b: the one thing in flight");

        var result = CliRunner.RunWithStdin(CompactPayload, "--vault", _vault.VaultPath, "agent-context");

        result.ExitCode.Should().Be(0);
        using var doc = JsonDocument.Parse(result.StdOut);
        var additionalContext = doc.RootElement
            .GetProperty("hookSpecificOutput")
            .GetProperty("additionalContext")
            .GetString()!;

        additionalContext.Should().NotContain("other open items",
            "the fixture must actually reach the nothing-left-over branch");
        additionalContext.Should().Contain("read_entry");
    }
}
