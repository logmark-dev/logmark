using System.Text.Json;
using FluentAssertions;
using LogMark.CLI;
using LogMark.CLI.Commands;
using LogMark.Services.Agents;
using LogMark.Services.Providers;
using Xunit;

namespace LogMark.CLI.Tests.Commands;

[Collection("EnvironmentTests")]
public class ConnectChoiceTests : IDisposable
{
    private readonly TextWriter _out = Console.Out;
    private readonly TextWriter _err = Console.Error;
    private readonly Dictionary<string, string?> _env = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lm-choice-{Guid.NewGuid():N}");
    private readonly string _home;
    private readonly string _vault;
    private readonly Func<bool> _savedInteractive = ConnectCommand.IsInteractive;
    private readonly Func<string?> _savedRead = ConnectCommand.ReadChoice;
    private readonly string _savedOwner = ConnectCommand.OwnerId;
    private readonly string _desktopMcp;

    public ConnectChoiceTests()
    {
        _home = Path.Combine(_root, "home");
        _vault = Path.Combine(_root, "vault");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_vault);
        Set("USERPROFILE", _home);
        Set("HOME", _home);
        Set("APPDATA", Path.Combine(_root, "roaming"));
        Set("LOCALAPPDATA", Path.Combine(_root, "local"));
        // Pin the provider and ledger roots inside the sandbox: where the platform
        // reports no ApplicationData folder, the stores fall back to a relative path.
        Set("LOGMARK_APPDATA_PATH", Path.Combine(_root, "appdata"));
        Set("PATH", Path.Combine(_root, "empty-path"));

        _desktopMcp = Path.Combine(_root, "desktop", "logmark-mcp.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_desktopMcp)!);
        File.WriteAllText(_desktopMcp, "");
        // The pinned bootstrap must exist, or the entry diagnoses as broken and
        // connect would repair (rewrite) it instead of recording an owner.
        File.WriteAllText(Path.Combine(_vault, "vault-bootstrap.json"), JsonSerializer.Serialize(new { VaultPath = _vault }));
        new ProviderManifestStore(ProviderManifestStore.DefaultRoot())
            .Save(ProviderSelfDescription.Desktop(_desktopMcp, _desktopMcp, "0.23.0"));

        // The desktop app's own legacy user-scope entry, as on rog-dev.
        File.WriteAllText(Path.Combine(_home, ".claude.json"), JsonSerializer.Serialize(new
        {
            mcpServers = new { logmark = new { command = _desktopMcp, args = new[] { "--bootstrap-path", Path.Combine(_vault, "vault-bootstrap.json"), "--agent", "claude-code" } } }
        }));

        ConnectCommand.OwnerId = "oss-cli";
        ConnectCommand.IsInteractive = () => false;
    }

    private void Set(string key, string? value)
    {
        _env[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public void Dispose()
    {
        Console.SetOut(_out);
        Console.SetError(_err);
        ConnectCommand.IsInteractive = _savedInteractive;
        ConnectCommand.ReadChoice = _savedRead;
        ConnectCommand.OwnerId = _savedOwner;
        foreach (var (k, v) in _env) Environment.SetEnvironmentVariable(k, v);
        try { Directory.Delete(_root, true); } catch { }
    }

    private (int Code, string Out, string Err) Run(ICommand cmd, bool json, params string[] args) =>
        RunIn(_vault, cmd, json, args);

    /// <summary>Runs with <paramref name="vault"/> as the resolved --vault.</summary>
    private (int Code, string Out, string Err) RunIn(string vault, ICommand cmd, bool json, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        Console.SetOut(o);
        Console.SetError(e);
        var code = cmd.Execute(new CliContext(vault, json, quiet: false), args);
        return (code, o.ToString(), e.ToString());
    }

    private string[] EntryArgs(out string command)
    {
        using var doc = JsonDocument.Parse(Config);
        var entry = doc.RootElement.GetProperty("mcpServers").GetProperty("logmark");
        command = entry.GetProperty("command").GetString()!;
        return entry.GetProperty("args").EnumerateArray().Select(a => a.GetString()!).ToArray();
    }

    [Fact]
    public void Connect_repoint_pins_the_entry_with_vault_override_and_exits_0()
    {
        var other = Path.Combine(_root, "other-vault");
        Directory.CreateDirectory(other);

        RunIn(other, new ConnectCommand(), false, "claude-code", "--mcp").Code
            .Should().Be(3, "without --repoint a different vault is a choice");
        var (code, _, err) = RunIn(other, new ConnectCommand(), false, "claude-code", "--mcp", "--repoint");

        code.Should().Be(0, err);
        var args = EntryArgs(out var command);
        args.Should().ContainInOrder("--vault-override", other);
        command.Should().Be(_desktopMcp, "the desktop's registered binary keeps serving the entry");
        RunIn(other, new ConnectCommand(), false, "claude-code", "--mcp").Code
            .Should().Be(0, "the entry now reaches the requested vault");
    }

    /// <summary>rc6 QA O2: a printed alternative must be safe to paste - it
    /// carries the --vault the sentence is about - and an exit-3 result never
    /// reads "ok".</summary>
    [Fact]
    public void A_choice_prints_a_pasteable_command_and_no_ok()
    {
        var other = Path.Combine(_root, "other-vault");
        Directory.CreateDirectory(other);

        var (code, stdout, _) = RunIn(other, new ConnectCommand(), false, "claude-code", "--mcp");

        code.Should().Be(3);
        stdout.Should().Contain($"connect claude-code --mcp --repoint --vault \"{other}\"");
        stdout.Should().NotMatchRegex(@"Mcp\s+Noop\s+ok");
        stdout.Should().MatchRegex(@"Mcp\s+Noop\s+unchanged");
    }

    [Fact]
    public void A_project_scope_choice_repeats_the_scope_and_project()
    {
        var project = Path.Combine(_root, "proj");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, ".mcp.json"), JsonSerializer.Serialize(new
        {
            mcpServers = new { logmark = new { command = Path.Combine(_root, "elsewhere", "my-mcp-server.exe"), args = new[] { "serve" } } }
        }));

        var (code, stdout, _) = Run(new ConnectCommand(), false, "claude-code", "--mcp", "--scope", "project", "--project", project);

        code.Should().Be(3);
        stdout.Should().Contain($"connect claude-code --mcp --replace --scope project --project \"{project}\" --vault \"{_vault}\"");
    }

    /// <summary>The MCP server reads the entry's own bootstrap file before the
    /// app bootstrap, so the comparison must too.</summary>
    [Fact]
    public void The_entrys_bootstrap_vault_beats_the_app_bootstrap_in_the_comparison()
    {
        var appVault = Path.Combine(_root, "app-vault");
        var entry = new McpEntryInfo(_desktopMcp, Path.Combine(_vault, "vault-bootstrap.json"), null);

        VaultResolver.ResolveEntryVault(entry, appBootstrap: () => appVault)
            .Should().Be(_vault, "the MCP server reads the entry's own bootstrap file before the app bootstrap");
        VaultResolver.ResolveEntryVault(new McpEntryInfo(_desktopMcp, null, null), appBootstrap: () => appVault)
            .Should().Be(appVault);
        VaultResolver.ResolveEntryVault(entry with { VaultOverride = appVault }, appBootstrap: () => null)
            .Should().Be(appVault);
    }

    [Fact]
    public void Connect_help_says_LOGMARK_VAULT_counts_as_vault_for_the_comparison()
    {
        var help = new ConnectCommand().DetailedHelp!;
        help.Should().Contain("LOGMARK_VAULT").And.Contain("--vault");
        help.Should().MatchRegex("LOGMARK_VAULT[^\\n]*compar|compar[^\\n]*LOGMARK_VAULT");
    }

    [Fact]
    public void Connect_replace_rewrites_a_custom_entry_and_exits_0()
    {
        var foreign = Path.Combine(_root, "tools", "other-memory");
        File.WriteAllText(Path.Combine(_home, ".claude.json"), JsonSerializer.Serialize(new
        {
            mcpServers = new { logmark = new { command = foreign, args = new[] { "serve" } } }
        }));

        RunIn(_vault, new ConnectCommand(), false, "claude-code", "--mcp").Code.Should().Be(3);
        // With the desktop app installed the Engine never writes the entry
        // (root design 2026-10-21 D7), not even with --replace.
        RunIn(_vault, new ConnectCommand(), false, "claude-code", "--mcp", "--replace").Code.Should().Be(3);
        EntryArgs(out var kept);
        kept.Should().Be(foreign);

        // Desktop uninstalled: the Engine writes its own `logmark mcp`.
        File.Delete(_desktopMcp);
        var engine = Path.Combine(_root, "engine", "logmark.exe");
        var saved = ConnectCommand.SelfMcpLaunch;
        ConnectCommand.SelfMcpLaunch = new McpLaunchSpec(engine, new[] { "mcp" });
        try
        {
            var (code, _, err) = RunIn(_vault, new ConnectCommand(), false, "claude-code", "--mcp", "--replace");

            code.Should().Be(0, err);
            EntryArgs(out var command);
            command.Should().Be(engine);
        }
        finally { ConnectCommand.SelfMcpLaunch = saved; }
    }

    private string Config => File.ReadAllText(Path.Combine(_home, ".claude.json"));

    /// <summary>The command prints the plan JSON, then the result JSON; each
    /// top-level object starts on a line that is exactly "{".</summary>
    private static JsonElement LastJsonObject(string stdout)
    {
        var lines = stdout.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindLastIndex(lines, l => l == "{");
        using var doc = JsonDocument.Parse(string.Join("\n", lines[start..]));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Disconnect_dry_run_writes_nothing_and_exits_like_the_real_run()
    {
        var before = Config;
        var (code, stdout, _) = Run(new DisconnectCommand(), false, "claude-code", "--mcp", "--dry-run");
        code.Should().Be(3, "the real run would change nothing and need a choice");
        stdout.Should().Contain("logmark disconnect claude-code --mcp --all-products");
        Config.Should().Be(before);
    }

    [Fact]
    public void Connect_dry_run_on_the_desktops_vault_exits_0()
    {
        Run(new ConnectCommand(), false, "claude-code", "--mcp", "--dry-run").Code.Should().Be(0);
    }

    [Fact]
    public void Unknown_flag_exits_64_and_writes_nothing()
    {
        var before = Config;
        var (code, _, err) = Run(new DisconnectCommand(), false, "claude-code", "--mcp", "--dryrun");
        code.Should().Be(64);
        err.Should().Contain("--dryrun");
        Config.Should().Be(before);
    }

    [Fact]
    public void Connect_only_flags_are_unknown_to_disconnect_and_vice_versa()
    {
        Run(new DisconnectCommand(), false, "claude-code", "--mcp", "--repoint").Code.Should().Be(64);
        Run(new ConnectCommand(), false, "claude-code", "--mcp", "--all-products").Code.Should().Be(64);
    }

    [Fact]
    public void Non_interactive_disconnect_of_the_desktops_entry_exits_3_and_prints_the_flag()
    {
        var before = Config;
        var (code, stdout, _) = Run(new DisconnectCommand(), false, "claude-code", "--mcp");
        code.Should().Be(3);
        Config.Should().Be(before);
        stdout.Should().Contain("the LogMark desktop app")
            .And.Contain("logmark disconnect claude-code --mcp --all-products");
    }

    [Fact]
    public void Non_interactive_run_never_reads_a_choice()
    {
        ConnectCommand.ReadChoice = () => throw new InvalidOperationException("prompted in a non-interactive run");
        Run(new DisconnectCommand(), false, "claude-code", "--mcp").Code.Should().Be(3);
    }

    [Fact]
    public void Json_result_carries_exit_code_and_choices()
    {
        var (code, stdout, _) = Run(new DisconnectCommand(), true, "claude-code", "--mcp");
        code.Should().Be(3);
        var result = LastJsonObject(stdout);
        result.GetProperty("exitCode").GetInt32().Should().Be(3);
        var item = result.GetProperty("items")[0];
        item.GetProperty("choiceNeeded").GetBoolean().Should().BeTrue();
        item.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("id").GetString())
            .Should().Contain("all-products");
    }

    [Fact]
    public void Interactive_choice_2_removes_for_every_product()
    {
        ConnectCommand.IsInteractive = () => true;
        ConnectCommand.ReadChoice = () => "2";
        var (code, stdout, _) = Run(new DisconnectCommand(), false, "claude-code", "--mcp");
        code.Should().Be(0);
        stdout.Should().Contain("1) Leave it as is (default)");
        Config.Should().NotContain("\"logmark\"");
    }

    [Fact]
    public void Interactive_empty_answer_takes_the_safe_default()
    {
        ConnectCommand.IsInteractive = () => true;
        ConnectCommand.ReadChoice = () => "";
        var before = Config;
        Run(new DisconnectCommand(), false, "claude-code", "--mcp").Code.Should().Be(3);
        Config.Should().Be(before);
    }

    [Fact]
    public void Connect_on_the_desktops_vault_reports_the_fallback_and_exits_0()
    {
        var before = Config;
        var (code, stdout, _) = Run(new ConnectCommand(), false, "claude-code", "--mcp");
        code.Should().Be(0);
        Config.Should().Be(before);
        stdout.Should().Contain("registered as a fallback");
    }
}
