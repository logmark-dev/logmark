using System.Text.Json;
using FluentAssertions;
using LogMark.CLI;
using LogMark.CLI.Commands;
using LogMark.Services.Agents;
using LogMark.Services.Providers;
using Xunit;

namespace LogMark.CLI.Tests.Commands;

/// <summary>
/// Tests ConnectCommand directly against a ConnectService built by
/// ConnectCommand.BuildConnectService, sandboxed the same way
/// ConnectServiceTests.TestConnect.Build() sandboxes the underlying wiring
/// services: USERPROFILE/APPDATA/LOCALAPPDATA redirected to a disposable temp
/// tree so HooksService/InboundWirer/SkillsService never touch the developer's
/// real home directory. Tagged EnvironmentTests (see HooksCommandTests) since
/// it mutates process-wide environment variables.
/// </summary>
[Collection("EnvironmentTests")]
public class ConnectCommandTests : IDisposable
{
    private readonly TextWriter _originalOut = Console.Out;
    private readonly TextWriter _originalErr = Console.Error;
    private readonly Dictionary<string, string?> _envSnapshot = new();
    private readonly string _root;
    private readonly string _home;

    public ConnectCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"lm-connectcmd-{Guid.NewGuid():N}");
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_root);
        OverrideEnv("USERPROFILE", _home);
        OverrideEnv("APPDATA", Path.Combine(_root, "appdata-roaming"));
        OverrideEnv("LOCALAPPDATA", Path.Combine(_root, "appdata-local"));
        OverrideEnv("LOGMARK_APPDATA_PATH", Path.Combine(_root, "appdata-logmark"));
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalErr);
        foreach (var (key, value) in _envSnapshot)
            Environment.SetEnvironmentVariable(key, value);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void OverrideEnv(string key, string value)
    {
        _envSnapshot[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
        Directory.CreateDirectory(value);
    }

    [Fact]
    public void No_capability_and_no_all_errors_exit_2()
    {
        var (cmd, ctx, err) = TestCli.Connect();
        cmd.Execute(ctx, new[] { "claude-code" }).Should().Be(2);
        err.ToString().Should().Contain("--hooks");
    }

    [Fact]
    public void No_capability_error_lists_all_four_flags()
    {
        var (cmd, ctx, err) = TestCli.Connect();
        cmd.Execute(ctx, new[] { "claude-code" }).Should().Be(2);
        var text = err.ToString();
        text.Should().Contain("--hooks").And.Contain("--mcp").And.Contain("--skills").And.Contain("--all");
    }

    [Fact]
    public void Dry_run_prints_plan_and_writes_nothing()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        cmd.Execute(ctx, new[] { "claude-code", "--all", "--dry-run" }).Should().Be(0);

        // The sandboxed home is where claude-code's hook file, MCP config, and
        // skills dir would land if the plan's read-only derivation wrote
        // anything - dry-run must call Plan() only, never Apply().
        Directory.EnumerateFileSystemEntries(_home).Should().BeEmpty("dry-run writes nothing");
    }

    [Fact]
    public void Dry_run_output_mentions_all_three_capabilities()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "claude-code", "--all", "--dry-run" }).Should().Be(0);

        var text = stdout.ToString();
        text.Should().Contain("Hooks").And.Contain("Mcp").And.Contain("Skills");
    }

    [Fact]
    public void Apply_without_dry_run_writes_hooks_and_reports_result()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        // Only --hooks: Mcp would fail in this sandbox (no logmark-mcp.exe
        // present under AppContext.BaseDirectory/mcp), which is fine for this
        // command's own concern (parsing + wiring the plan/apply/exit-code
        // contract) - ConnectService's own tests cover per-capability
        // failure isolation.
        var exitCode = cmd.Execute(ctx, new[] { "claude-code", "--hooks" });

        exitCode.Should().Be(0);
        Directory.EnumerateFileSystemEntries(_home).Should().NotBeEmpty("Apply must write the hook");
        stdout.ToString().Should().Contain("Result for claude-code");
    }

    [Fact]
    public void Json_output_emits_plan_as_json_on_dry_run()
    {
        var (cmd, ctx, _) = TestCli.Connect(jsonOutput: true);
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "claude-code", "--all", "--dry-run" }).Should().Be(0);

        var text = stdout.ToString();
        text.Should().Contain("\"framework\"").And.Contain("claude-code");
    }

    [Fact]
    public void Mcp_apply_without_resolvable_vault_errors_exit_2_and_writes_nothing()
    {
        // No --vault, no LOGMARK_VAULT - CliContext.VaultPath is null. Without
        // the guard, InboundWirer would be constructed with activeVaultPath =
        // string.Empty and Apply's ApplyMcp -> Connect(PinCurrent) call would
        // write a stray relative "vault-bootstrap.json" resolved against the
        // process's current directory. Redirect CWD to a disposable sandbox
        // folder so the assertion doesn't depend on (or pollute) wherever the
        // test host's real working directory happens to be.
        var (cmd, ctx, err) = TestCli.Connect();
        var cwdSandbox = Path.Combine(_root, "cwd");
        Directory.CreateDirectory(cwdSandbox);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = cwdSandbox;
        try
        {
            var exitCode = cmd.Execute(ctx, new[] { "claude-code", "--mcp" });

            exitCode.Should().Be(2);
            err.ToString().Should().Contain("vault");
            Directory.EnumerateFileSystemEntries(cwdSandbox).Should().BeEmpty(
                "a vault-less --mcp apply must not write vault-bootstrap.json (or anything else) into the process CWD");
            Directory.EnumerateFileSystemEntries(_home).Should().BeEmpty(
                "no wiring service should touch the sandboxed home either");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }

    [Fact]
    public void ParseFlags_project_value_equal_to_route_token_is_not_reparsed_as_route()
    {
        // Regression test for a semantic-drift bug (commit 9394373d): a prior
        // dedup refactor scanned args with TWO independent loops both
        // starting at the same index (ParseCommonFlags for the shared flags,
        // a second connect-only loop for --dry-run/--route). Because neither
        // loop knew which indices the other had already consumed as a flag's
        // VALUE, a --project value that literally equalled the token
        // "--route" got re-matched by the second loop, incorrectly setting
        // route (to the trailing "x") where the original single-loop code
        // should leave it null. The trailing token is what makes the old
        // two-loop guard (i + 1 < args.Length) succeed and mis-capture, so it
        // is required for this test to discriminate the bug. ParseFlags is now
        // the single scan for both commands, so "--route" here must be consumed
        // once, as --project's value, and never re-examined as a flag.
        var flags = ConnectCommand.ParseFlags(
            new[] { "fw", "--project", "--route", "x" }, 1, ConnectCommand.ConnectFlagNames);

        flags.Project.Should().Be("--route");
        flags.Route.Should().BeNull("the trailing 'x' must not leak into route via a second scan");
        flags.DryRun.Should().BeFalse();
        flags.Unknown.Should().Equal("x");
    }

    [Fact]
    public void Invalid_scope_value_errors_exit_2()
    {
        var (cmd, ctx, err) = TestCli.Connect();
        cmd.Execute(ctx, new[] { "claude-code", "--hooks", "--scope", "bogus" }).Should().Be(2);
        err.ToString().Should().Contain("--scope");
    }

    [Fact]
    public void Unknown_framework_slug_errors_exit_2_with_valid_slug_list()
    {
        // Regression test for the "typo silently exits 0" bug: an entirely
        // unrecognized slug used to plan every capability Unsupported, and
        // Apply carried each through as Success=true, so AnyFailure stayed
        // false and the command exited 0 even though nothing was wired.
        var (cmd, ctx, err) = TestCli.Connect();
        var exitCode = cmd.Execute(ctx, new[] { "cluade-code", "--all" });

        exitCode.Should().Be(2);
        var text = err.ToString();
        text.Should().Contain("cluade-code");
        foreach (var slug in HookProfileRegistry.SupportedSlugs)
            text.Should().Contain(slug);
    }

    [Fact]
    public void Unknown_framework_slug_writes_nothing_to_stdout()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "cluade-code", "--all" }).Should().Be(2);

        stdout.ToString().Should().BeEmpty("an unrecognized slug must fail before any plan is printed");
    }

    [Fact]
    public void Known_framework_with_partially_unsupported_capability_still_exits_0()
    {
        // gemini-cli is a real HookProfileRegistry entry (so IsKnownFramework
        // is true and the unknown-slug guard does not fire), but it has no
        // AgentRegistry entry, so its Skills capability plans Unsupported.
        // That per-capability Unsupported must not be conflated with the
        // unknown-framework case - a known framework missing one capability
        // is a normal, successful outcome (exit 0), not a failure.
        var (cmd, ctx, err) = TestCli.Connect();
        var exitCode = cmd.Execute(ctx, new[] { "gemini-cli", "--skills" });

        exitCode.Should().Be(0);
        err.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Mcp_apply_without_resolvable_vault_writes_nothing_to_stdout()
    {
        // Regression test for the guard-atomicity fix: the --mcp vault guard
        // used to fire AFTER Plan()/WritePlan(), so a JSON (or text) consumer
        // saw a plan on stdout followed by an error with no matching result.
        // The guard is now hoisted above Plan/WritePlan for the non-dry-run
        // apply path, so nothing should reach stdout before the error.
        var (cmd, ctx, err) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        var cwdSandbox = Path.Combine(_root, "cwd-stdout-guard");
        Directory.CreateDirectory(cwdSandbox);
        var originalCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = cwdSandbox;
        try
        {
            var exitCode = cmd.Execute(ctx, new[] { "claude-code", "--mcp" });

            exitCode.Should().Be(2);
            err.ToString().Should().Contain("vault");
            stdout.ToString().Should().BeEmpty("nothing should print before the guard fires");
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
        }
    }

    [Fact]
    public void Disconnect_no_capability_and_no_all_errors_exit_2()
    {
        var (cmd, ctx, err) = TestCli.Disconnect();
        cmd.Execute(ctx, new[] { "claude-code" }).Should().Be(2);
        err.ToString().Should().Contain("--hooks");
    }

    [Fact]
    public void Disconnect_invalid_scope_value_errors_exit_2()
    {
        var (cmd, ctx, err) = TestCli.Disconnect();
        cmd.Execute(ctx, new[] { "claude-code", "--hooks", "--scope", "bogus" }).Should().Be(2);
        err.ToString().Should().Contain("--scope");
    }

    [Fact]
    public void Disconnect_unknown_framework_slug_errors_exit_2_with_valid_slug_list()
    {
        var (cmd, ctx, err) = TestCli.Disconnect();
        var exitCode = cmd.Execute(ctx, new[] { "cluade-code", "--all" });

        exitCode.Should().Be(2);
        var text = err.ToString();
        text.Should().Contain("cluade-code");
        foreach (var slug in HookProfileRegistry.SupportedSlugs)
            text.Should().Contain(slug);
    }

    [Fact]
    public void Disconnect_without_vault_does_not_require_one()
    {
        // Disconnect must not apply ConnectCommand's --mcp-needs-a-vault guard:
        // InboundWirer.Disconnect only resolves the agent's config path and
        // removes the logmark entry from it - it never reads _activeVaultPath,
        // so unlike Connect's Apply it has nothing that would break with a
        // vault-less (string.Empty) InboundWirer. No vault is configured here
        // (VaultPath: null) and the call must still succeed.
        var (cmd, ctx, err) = TestCli.Disconnect();
        var exitCode = cmd.Execute(ctx, new[] { "claude-code", "--mcp" });

        exitCode.Should().Be(0);
        err.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Disconnect_after_connect_all_leaves_nothing_installed()
    {
        // connect --all needs a resolvable vault to get past the --mcp guard
        // (mcp itself will still fail to Apply in this sandbox - no
        // logmark-mcp.exe under AppContext.BaseDirectory/mcp - but that failure
        // happens before any file is written, per InboundWirer.Connect). Hooks
        // and Skills apply for real.
        var vaultPath = Path.Combine(_root, "vault");
        Directory.CreateDirectory(vaultPath);

        var (connectCmd, connectCtx, _) = TestCli.Connect(vaultPath: vaultPath);
        connectCmd.Execute(connectCtx, new[] { "claude-code", "--all" });
        Directory.EnumerateFileSystemEntries(_home).Should().NotBeEmpty(
            "connect --all must have installed hooks and/or skills before disconnect runs");

        var (disconnectCmd, disconnectCtx, _) = TestCli.Disconnect(vaultPath: vaultPath);
        var exitCode = disconnectCmd.Execute(disconnectCtx, new[] { "claude-code", "--all" });

        exitCode.Should().Be(0);

        var (statusCmd, statusCtx, _) = TestCli.Connect(vaultPath: vaultPath, jsonOutput: true);
        var stdout = new StringWriter();
        Console.SetOut(stdout);
        statusCmd.Execute(statusCtx, new[] { "--status", "claude-code" }).Should().Be(0);

        var json = stdout.ToString();
        json.Should().NotContain("\"installed\":true", "disconnect --all must leave nothing installed");
    }

    [Fact]
    public void Status_json_emits_per_capability_state_array()
    {
        var (cmd, ctx, _) = TestCli.Connect(jsonOutput: true);
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "--status", "claude-code" }).Should().Be(0);

        var json = stdout.ToString();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(3, "Hooks, Mcp, Skills");
        json.Should().Contain("\"capability\"").And.Contain("\"installed\"").And.Contain("\"supported\"");
    }

    [Fact]
    public void Status_text_output_reports_all_three_capabilities()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "--status", "claude-code" }).Should().Be(0);

        var text = stdout.ToString();
        text.Should().Contain("Hooks").And.Contain("Mcp").And.Contain("Skills");
    }

    [Fact]
    public void Status_without_framework_iterates_all_supported_slugs()
    {
        var (cmd, ctx, _) = TestCli.Connect();
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "--status" }).Should().Be(0);

        var text = stdout.ToString();
        foreach (var slug in HookProfileRegistry.SupportedSlugs)
            text.Should().Contain(slug);
    }

    [Fact]
    public void Scope_local_is_accepted_and_keys_on_the_project_dir()
    {
        var vaultPath = Path.Combine(_root, "vault");
        Directory.CreateDirectory(vaultPath);
        RegisterStubMcpProvider();
        var (cmd, ctx, _) = TestCli.Connect(jsonOutput: false, vaultPath: vaultPath);
        var projectDir = Path.Combine(_home, "repo");
        Directory.CreateDirectory(projectDir);

        cmd.Execute(ctx, new[] { "claude-code", "--mcp", "--scope", "local", "--project", projectDir }).Should().Be(0);

        var json = File.ReadAllText(Path.Combine(_home, ".claude.json"));
        json.Should().Contain(ClaudeProjectKey.Normalize(projectDir));
    }

    [Fact]
    public void Status_local_scope_prints_the_non_inheritance_note()
    {
        var (cmd, ctx, _) = TestCli.Connect(jsonOutput: false);
        var projectDir = Path.Combine(_home, "repo");
        Directory.CreateDirectory(projectDir);
        var stdout = new StringWriter();
        Console.SetOut(stdout);

        cmd.Execute(ctx, new[] { "--status", "claude-code", "--scope", "local", "--project", projectDir }).Should().Be(0);

        stdout.ToString().Should().Contain("(Local)")
            .And.Contain("does not inherit into subfolders");
    }

    /// <summary>
    /// Registers a placeholder logmark-mcp.exe as a live "desktop" provider in
    /// a sandboxed provider registry, so `connect --mcp` resolves an MCP
    /// winner. ProviderManifestStore.DefaultRoot() goes through
    /// Environment.GetFolderPath, which ignores the APPDATA override above, so
    /// the registry is redirected with LOGMARK_APPDATA_PATH instead - never
    /// the developer's real one.
    /// </summary>
    private void RegisterStubMcpProvider()
    {
        var exeDir = Path.Combine(_root, "installed");
        Directory.CreateDirectory(exeDir);
        var exe = Path.Combine(exeDir, "logmark-mcp.exe");
        File.WriteAllText(exe, "stub");
        new ProviderManifestStore(ProviderManifestStore.DefaultRoot()).Save(
            ProviderSelfDescription.Desktop(exe, exe, "1.0.0"));
    }

    /// <summary>
    /// Builds a ConnectCommand + CliContext pair with Console.Out/Console.Error
    /// redirected to captured StringWriters, so tests can call cmd.Execute()
    /// directly (bypassing CommandRegistry/CommandRunner dispatch) and assert
    /// on stdout/stderr content. No vault is configured (VaultPath: null) -
    /// this command's RequiresVault is false and the tests exercised here
    /// (capability-selection error, dry-run, hooks-only apply) don't need one.
    /// </summary>
    private static class TestCli
    {
        public static (ConnectCommand Cmd, CliContext Ctx, StringWriter Err) Connect(
            bool jsonOutput = false, string? vaultPath = null)
        {
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var ctx = new CliContext(vaultPath: vaultPath, jsonOutput: jsonOutput, quiet: false);
            return (new ConnectCommand(), ctx, errWriter);
        }

        public static (DisconnectCommand Cmd, CliContext Ctx, StringWriter Err) Disconnect(
            bool jsonOutput = false, string? vaultPath = null)
        {
            var outWriter = new StringWriter();
            var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var ctx = new CliContext(vaultPath: vaultPath, jsonOutput: jsonOutput, quiet: false);
            return (new DisconnectCommand(), ctx, errWriter);
        }
    }
}
