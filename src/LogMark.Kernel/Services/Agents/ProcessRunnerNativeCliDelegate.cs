namespace LogMark.Services.Agents;

public class ProcessRunnerNativeCliDelegate : INativeCliDelegate
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _runner;

    public ProcessRunnerNativeCliDelegate(IProcessRunner runner) => _runner = runner;

    public bool IsAvailable(NativeWireCli cli) => _runner.IsOnPath(cli.Executable);

    public WiringResult Add(NativeWireCli cli, AgentDefinition agent, WireScope scope,
        string mcpExePath, string[] mcpArgs, string? projectFolderPath)
    {
        var argv = BuildAddArgv(cli, scope, mcpExePath, mcpArgs);
        return ExecuteAtPath(cli, argv, projectFolderPath);
    }

    public WiringResult Remove(NativeWireCli cli, AgentDefinition agent, WireScope scope,
        string? projectFolderPath)
    {
        var argv = BuildRemoveArgv(cli, scope);
        return ExecuteAtPath(cli, argv, projectFolderPath);
    }

    private static string[] BuildAddArgv(NativeWireCli cli, WireScope scope,
        string mcpExePath, string[] mcpArgs) => cli.Shape switch
    {
        NativeCliShape.ClaudeMcpAdd => BuildClaudeAdd(cli, scope, mcpExePath, mcpArgs),
        NativeCliShape.VsCodeAddMcp => BuildVsCodeAdd(mcpExePath, mcpArgs),
        NativeCliShape.CodexMcpAdd => BuildCodexAdd(mcpExePath, mcpArgs),
        NativeCliShape.AmazonQMcpAdd => BuildQAdd(cli, scope, mcpExePath, mcpArgs),
        _ => throw new ArgumentException($"Unknown shape: {cli.Shape}")
    };

    private static string[] BuildClaudeAdd(NativeWireCli cli, WireScope scope,
        string mcpExePath, string[] mcpArgs)
    {
        var scopeFlag = scope == WireScope.Project ? cli.ScopeFlagProject : cli.ScopeFlagUser;
        var scopeParts = scopeFlag?.Split(' ') ?? Array.Empty<string>();
        var argv = new List<string> { "mcp", "add" };
        argv.AddRange(scopeParts);
        argv.AddRange(new[] { "logmark", "--transport", "stdio", "--" });
        argv.Add(mcpExePath);
        argv.AddRange(mcpArgs);
        return argv.ToArray();
    }

    private static string[] BuildVsCodeAdd(string mcpExePath, string[] mcpArgs)
    {
        var server = new
        {
            name = "logmark",
            command = mcpExePath,
            args = mcpArgs,
            env = new Dictionary<string, string>()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(server);
        return new[] { "--add-mcp", json };
    }

    private static string[] BuildCodexAdd(string mcpExePath, string[] mcpArgs)
    {
        var argv = new List<string> { "mcp", "add", "logmark", "--" };
        argv.Add(mcpExePath);
        argv.AddRange(mcpArgs);
        return argv.ToArray();
    }

    private static string[] BuildQAdd(NativeWireCli cli, WireScope scope,
        string mcpExePath, string[] mcpArgs)
    {
        var scopeFlag = scope == WireScope.Project ? cli.ScopeFlagProject : cli.ScopeFlagUser;
        var scopeParts = scopeFlag?.Split(' ') ?? Array.Empty<string>();
        var argv = new List<string>
        {
            "mcp", "add",
            "--name", "logmark"
        };
        argv.AddRange(scopeParts);
        argv.AddRange(new[] { "--command", mcpExePath });
        argv.Add("--args");
        argv.Add(System.Text.Json.JsonSerializer.Serialize(mcpArgs));
        return argv.ToArray();
    }

    private static string[] BuildRemoveArgv(NativeWireCli cli, WireScope scope) => cli.Shape switch
    {
        // Always name the scope: an unscoped `claude mcp remove` takes the
        // first entry it resolves (local, then project, then user), which is
        // how a project disconnect removed the user entry (bead lmk-1yd8).
        NativeCliShape.ClaudeMcpAdd => BuildScopedRemove(cli, scope, "logmark"),
        NativeCliShape.CodexMcpAdd => new[] { "mcp", "remove", "logmark" },
        NativeCliShape.AmazonQMcpAdd => BuildScopedRemove(cli, scope, "--name", "logmark"),
        NativeCliShape.VsCodeAddMcp => Array.Empty<string>(),  // VS Code has no remove; falls back to file writer
        _ => Array.Empty<string>()
    };

    private static string[] BuildScopedRemove(NativeWireCli cli, WireScope scope,
        params string[] target)
    {
        var scopeFlag = scope == WireScope.Project ? cli.ScopeFlagProject : cli.ScopeFlagUser;
        var argv = new List<string> { "mcp", "remove" };
        argv.AddRange(target);
        argv.AddRange(scopeFlag?.Split(' ') ?? Array.Empty<string>());
        return argv.ToArray();
    }

    private WiringResult ExecuteAtPath(NativeWireCli cli, string[] argv, string? projectFolderPath)
    {
        if (argv.Length == 0)
            return new WiringResult(false, "Native CLI does not support remove for this client");

        // projectFolderPath is unused: the CLI runs in this process's cwd, so InboundWirer
        // delegates user scope only (beads lmk-xw1e, lmk-1yd8). Delegating a project or
        // local scope would need IProcessRunner.Run to take a working directory first.
        ProcessResult result;
        try
        {
            result = _runner.Run(cli.Executable, argv, Timeout);
        }
        catch (Exception ex)
        {
            // Process.Start throws (Win32Exception "cannot find the file specified") when the
            // executable can't be launched -- e.g. the CLI resolved to an npm .cmd shim that
            // UseShellExecute=false can't exec, or the binary vanished after the availability
            // probe. Degrade to a soft failure so Connect/Disconnect fall back to the file-writer
            // path instead of crashing the app on the dispatcher thread.
            return new WiringResult(false, $"{cli.Executable} could not be launched: {ex.Message}");
        }
        if (result.TimedOut)
            return new WiringResult(false, $"{cli.Executable} {cli.AddSubcommand} timed out after 30s");
        if (result.ExitCode != 0)
            return new WiringResult(false, $"{cli.Executable} {cli.AddSubcommand} failed: {result.Stderr.Trim()}");
        return new WiringResult(true);
    }
}
