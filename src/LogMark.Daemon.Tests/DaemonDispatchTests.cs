using System.Text.RegularExpressions;
using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// Serializes the console-capturing dispatch tests against every other
/// collection: they redirect the process-global Console.Out/Error, so nothing
/// else may run concurrently.
/// </summary>
[CollectionDefinition("ConsoleCapture", DisableParallelization = true)]
public class ConsoleCaptureCollection { }

/// <summary>
/// Covers the unified `logmark` binary's argv[0] dispatcher
/// (<see cref="Program.Main"/>): version/help/unknown handling and delegation
/// to the open CLI, none of which boots the serve stack. All tests capture
/// Console.Out/Error, so they live in one class (xunit serializes tests within
/// a class) to avoid interleaving the process-global console streams.
/// </summary>
[Collection("ConsoleCapture")]
public class DaemonDispatchTests
{
    private static (int code, string stdout, string stderr) RunCli(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var origOut = Console.Out;
        var origErr = Console.Error;
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var code = Program.Main(args).GetAwaiter().GetResult();
            return (code, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Version_flag_prints_a_version_string()
    {
        var (code, stdout, _) = RunCli("--version");

        Assert.Equal(0, code);
        Assert.Matches(new Regex(@"^LogMark Engine \d+\.\d+\.\d+"), stdout.Trim());
    }

    [Fact]
    public void Unknown_verb_exits_64_and_prints_usage()
    {
        var (code, stdout, stderr) = RunCli("frobnicate");

        Assert.Equal(64, code);
        var all = stdout + stderr;
        Assert.Contains("unknown command", all);
        // Usage lists both host verbs so the user can recover.
        Assert.Contains("serve", all);
        Assert.Contains("mcp", all);
    }

    [Fact]
    public void Bare_invocation_exits_64_with_usage()
    {
        var (code, stdout, stderr) = RunCli();

        Assert.Equal(64, code);
        var all = stdout + stderr;
        Assert.Contains("serve", all);
        Assert.Contains("mcp", all);
    }

    [Fact]
    public void Help_flag_prints_usage_and_exits_zero()
    {
        var (code, stdout, _) = RunCli("--help");

        Assert.Equal(0, code);
        Assert.Contains("Usage: logmark", stdout);
        Assert.Contains("serve", stdout);
        Assert.Contains("mcp", stdout);
        // The CLI verb list is folded into the unified usage.
        Assert.Contains("recent", stdout);
    }

    [Fact]
    public void Serve_help_works_without_requiring_a_vault()
    {
        var (code, stdout, _) = RunCli("serve", "--help");

        Assert.Equal(0, code);
        Assert.Contains("serve --vault", stdout);
    }

    [Fact]
    public void A_cli_verb_round_trips_through_the_dispatcher_without_booting_serve()
    {
        // `version` is a vault-free CLI verb. Delegating to LogMark.CLI under the
        // Engine owner id prints the Engine banner, exit 0, no serve loop entered.
        var (code, stdout, _) = RunCli("version");

        Assert.Equal(0, code);
        Assert.StartsWith("LogMark Engine ", stdout.Trim());
    }

    [Fact]
    public void Usage_header_names_the_engine()
    {
        var (_, stdout, _) = RunCli("--help");
        Assert.StartsWith("LogMark Engine ", stdout);
    }

    [Fact]
    public void Version_verb_and_version_flag_agree()
    {
        var (_, flagOut, _) = RunCli("--version");
        var (_, verbOut, _) = RunCli("version");
        var flagVersion = flagOut.Trim().Split(' ').Last();
        Assert.Contains(flagVersion, verbOut);
    }

    [Fact]
    public void Single_word_cli_verb_is_recognized_not_treated_as_unknown()
    {
        // `tasks` requires a vault; pointing it at a nonexistent one makes the
        // CLI exit 1 (vault not found). The dispatcher must reach that path -
        // NOT its own unknown-verb path (64) - proving `tasks` is recognized as
        // a real CLI verb and delegated rather than rejected.
        var missingVault = Path.Combine(Path.GetTempPath(), "lm-no-such-vault-" + Guid.NewGuid().ToString("N")[..8]);
        var (code, _, _) = RunCli("tasks", "--vault", missingVault);

        Assert.NotEqual(64, code);
    }

    // --- Explicit-verbs-only (controller ruling, chunk A review) ---------

    /// <summary>
    /// Regression proof for the CRITICAL finding: the old dispatcher called
    /// <c>LogMark.CLI.CommandRunner.LooksLikeCapture(verb, args[1..])</c>
    /// directly on argv[0], which returns true for ANY multi-token argv
    /// (its "has remaining args" branch does not check whether those
    /// remaining tokens are flags or real content) - so `logmark --port 4747`
    /// or `logmark srve --vault X` fell through to
    /// <c>LogMark.CLI.Program.Run</c>, which resolved a real vault (flag, env
    /// var, or the developer's bootstrap.json) and wrote a garbage log entry
    /// to it, exit 0.
    ///
    /// Proof approach: point <c>LOGMARK_VAULT</c> (VaultResolver's tier-2
    /// fallback, used whenever the invocation carries no --vault flag of its
    /// own) at a freshly created, empty temp directory for the duration of
    /// the call. If the old bug regressed, the CLI capture path would
    /// resolve THIS directory (never the developer's real bootstrap vault)
    /// and write a file into it. Asserting the directory stays completely
    /// empty afterward is a direct filesystem proof - stronger than
    /// string-matching CLI output, which could pass for the wrong reason
    /// (e.g. capture failing for some unrelated cause and never reaching a
    /// "Saved" line despite still having run). The output-string assertion
    /// is kept too, as a cheap secondary signal.
    /// </summary>
    private static void AssertRejectedWithoutVaultWrite(params string[] args)
    {
        var tempVault = Path.Combine(Path.GetTempPath(), "lm-vault-write-proof-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempVault);
        var origVaultEnv = Environment.GetEnvironmentVariable("LOGMARK_VAULT");
        try
        {
            Environment.SetEnvironmentVariable("LOGMARK_VAULT", tempVault);

            var (code, stdout, stderr) = RunCli(args);

            Assert.Equal(64, code);
            var all = stdout + stderr;
            Assert.DoesNotContain("Saved ", all); // CaptureCommand's own success line
            Assert.Empty(Directory.GetFileSystemEntries(tempVault));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOGMARK_VAULT", origVaultEnv);
            try { Directory.Delete(tempVault, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Flag_first_invocation_is_rejected_not_captured()
    {
        // `logmark --port 4747` - a bare flag as argv[0], no recognized verb.
        AssertRejectedWithoutVaultWrite("--port", "4747");
    }

    [Fact]
    public void Multi_token_typo_verb_is_rejected_not_captured()
    {
        // `logmark srve --vault X` - a typo'd verb with a flag pair
        // following it (the flag pair is exactly the shape that used to
        // fool the old dispatcher's multi-token heuristic).
        AssertRejectedWithoutVaultWrite("srve", "--vault", "unused-because-rejected-before-any-vault-parsing");
    }

    [Fact]
    public void Rejected_unknown_command_hints_at_explicit_capture()
    {
        var (code, stdout, stderr) = RunCli("--port", "4747");

        Assert.Equal(64, code);
        var all = stdout + stderr;
        Assert.Contains("logmark capture", all);
    }

    [Fact]
    public void Uppercase_host_verb_still_matches_case_insensitively()
    {
        // `logmark SERVE --help` - the explicit-verbs-only gate matches known
        // host verbs case-insensitively (mirrors the CLI's own OrdinalIgnoreCase
        // command registry), not only the exact lowercase spelling. Uses
        // --help so the test returns instead of entering the serve loop.
        var (code, stdout, _) = RunCli("SERVE", "--help");

        Assert.Equal(0, code);
        Assert.Contains("serve --vault", stdout);
    }

    [Fact]
    public void Help_serve_routes_to_the_serve_usage_printer()
    {
        var (code, stdout, _) = RunCli("help", "serve");

        Assert.Equal(0, code);
        Assert.Contains("serve --vault", stdout);
    }

    [Fact]
    public void Help_mcp_routes_to_the_mcp_usage_printer()
    {
        var (code, stdout, _) = RunCli("help", "mcp");

        Assert.Equal(0, code);
        Assert.Contains("logmark mcp", stdout);
    }

    [Fact]
    public void Help_verb_and_target_match_case_insensitively()
    {
        var (code, stdout, _) = RunCli("HELP", "SERVE");

        Assert.Equal(0, code);
        Assert.Contains("serve --vault", stdout);
    }

    [Fact]
    public void Help_for_an_unrecognized_target_still_falls_through_to_the_cli()
    {
        // `help` targets that are neither "serve" nor "mcp" keep the
        // pre-existing behavior: delegate to the CLI's own command help
        // (or its "unknown command" handling), unchanged by this fix.
        var (code, stdout, stderr) = RunCli("help", "recent");

        Assert.Equal(0, code);
        Assert.Contains("recent", stdout + stderr);
    }

    [Fact]
    public void Vault_flag_before_verb_dispatches_the_verb_against_that_vault()
    {
        var vault = MakeInitializedVault();
        try
        {
            var (code, stdout, _) = RunCli("--vault", vault, "routes");
            Assert.Equal(0, code);
            Assert.Contains("arch", stdout); // build bundle's routes.md declares it
        }
        finally { Directory.Delete(vault, recursive: true); }
    }

    [Fact]
    public void Vault_flag_after_verb_is_honored_not_ignored()
    {
        var vault = MakeInitializedVault();
        var origEnv = Environment.GetEnvironmentVariable("LOGMARK_VAULT");
        try
        {
            // Point the env fallback somewhere EMPTY so a silently-ignored
            // flag would produce a visible failure, not a lucky pass.
            var decoy = Path.Combine(Path.GetTempPath(), "lm-decoy-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(decoy);
            Environment.SetEnvironmentVariable("LOGMARK_VAULT", decoy);

            var (code, stdout, _) = RunCli("routes", "--vault", vault);
            Assert.Equal(0, code);
            Assert.Contains("arch", stdout);
            Directory.Delete(decoy, recursive: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LOGMARK_VAULT", origEnv);
            Directory.Delete(vault, recursive: true);
        }
    }

    [Fact]
    public void Vault_flag_with_no_verb_exits_64()
    {
        AssertRejectedWithoutVaultWrite("--vault", Path.GetTempPath());
    }

    private static string MakeInitializedVault()
    {
        var vault = Path.Combine(Path.GetTempPath(), "lm-dispatch-" + Guid.NewGuid().ToString("N")[..8]);
        // The assertions depend on the build bundle's routes.md (the arch route); the OSS default is the empty open bundle.
        var (code, _, stderr) = RunCli("vault", "init", vault, "--bundle=build", "--no-seed");
        Assert.True(code == 0, "vault init failed: " + stderr);
        return vault;
    }

    [Fact]
    public void FindVerbIndex_skips_leading_vault_flag_and_value()
        => Assert.Equal(2, Program.FindVerbIndex(new[] { "--vault", "X", "mcp", "--agent", "a" }));

    [Fact]
    public void FindVerbIndex_flag_only_is_minus_one()
        => Assert.Equal(-1, Program.FindVerbIndex(new[] { "--vault", "X" }));

    [Fact]
    public void FindVerbIndex_trailing_valueless_vault_is_minus_one()
        => Assert.Equal(-1, Program.FindVerbIndex(new[] { "--vault" }));

    [Fact]
    public void FindVerbIndex_bare_flag_then_verb()
        => Assert.Equal(1, Program.FindVerbIndex(new[] { "--json", "serve" }));

    [Fact]
    public void Vault_flag_before_serve_slices_args_relative_to_the_verb()
    {
        var (code, stdout, _) = RunCli("--vault", Path.GetTempPath(), "serve", "--help");

        Assert.Equal(0, code);
        Assert.Contains("Usage: logmark serve", stdout);
    }

    [Fact]
    public void ForwardLeadingVault_Serve_ForwardsLeadingVault()
    {
        var a = new[] { "--vault", "X", "serve", "--port", "1" };
        Assert.Equal(new[] { "serve", "--vault", "X", "--port", "1" }, Program.ForwardLeadingVault(a, 2, keepVerb: true));
    }

    [Fact]
    public void ForwardLeadingVault_Mcp_ForwardsLeadingVault()
    {
        var a = new[] { "--vault", "X", "mcp", "--agent", "a" };
        Assert.Equal(new[] { "--vault", "X", "--agent", "a" }, Program.ForwardLeadingVault(a, 2, keepVerb: false));
    }

    [Fact]
    public void ForwardLeadingVault_TrailingVaultStaysLast()
    {
        Assert.Equal(new[] { "--vault", "X", "--vault", "Y" }, Program.ForwardLeadingVault(new[] { "--vault", "X", "mcp", "--vault", "Y" }, 2, false));
        Assert.Equal(new[] { "serve", "--vault", "Y" }, Program.ForwardLeadingVault(new[] { "--vault", "X", "serve", "--vault", "Y" }, 2, true));
    }

    [Fact]
    public void ForwardLeadingVault_LastLeadingWins_AndNoLeadingVaultUnchanged()
    {
        Assert.Equal(new[] { "serve", "--vault", "B" }, Program.ForwardLeadingVault(new[] { "--vault", "A", "--vault", "B", "serve" }, 4, true));
        Assert.Equal(new[] { "serve", "--port", "1" }, Program.ForwardLeadingVault(new[] { "serve", "--port", "1" }, 0, true));
        Assert.Equal(new[] { "--agent", "a" }, Program.ForwardLeadingVault(new[] { "--json", "mcp", "--agent", "a" }, 1, false));
    }

    [Fact]
    public void LeadingVault_BeforeServe_IsHonoured()
    {
        var missing = Path.Combine(Path.GetTempPath(), "logmark-nonexistent-" + Guid.NewGuid());
        var (code, _, stderr) = RunCli("--vault", missing, "serve");
        Assert.Equal(2, code);
        Assert.DoesNotContain("--vault <path> is required", stderr);
        Assert.Contains("vault not found", stderr);
    }
}
