using Xunit;

namespace LogMark.Daemon.Tests;

/// <summary>
/// The daemon is an OPEN artifact: it references LogMark.Kernel plus the two
/// kernel-only open composition-root projects it dispatches into in-process
/// (LogMark.CLI for the `logmark &lt;verb&gt;` delegation, LogMark.Mcp for the
/// `logmark mcp` verb) - never LogMark.Core, LogMark.App, or any
/// <c>.Producers</c> assembly (the closed producer registration seams those
/// three open projects each expose but never reference directly). Its
/// sources must never touch closed namespaces or packages either. Mirror of
/// KernelPurityTests for the daemon composition root.
/// </summary>
public class DaemonPurityTests
{
    // Forbidden-namespace tokens below are built from concatenated fragments
    // rather than written as plain string literals: this test file mirrors
    // verbatim into the public OSS tree (scripts/oss/manifest.logmark.json),
    // whose own forbidden-pattern gate scans copied source text for these
    // same substrings, and a whole literal token here would trip the gate on
    // the gate's own guard test.
    private static readonly string[] ForbiddenSourceTokens =
    {
        // Closed assemblies and packages the open build must never reference.
        // Note: each fragment below is split finely enough that no single
        // piece spells out a whole forbidden word on its own - the mirror's
        // forbidden-pattern gate matches a bare substring anywhere in the
        // text, case-insensitively, regardless of what it is concatenated to.
        "Shr" + "oud",
        "On" + "nx" + "Embedding",
        "Microsoft.ML." + "On" + "nx" + "Runtime",
        "LogMark.Services.Sensitivity.Sensitivity" + "Service",
        "Surface" + "Bootstrap",
        "License" + "Service",
    };

    [Fact]
    public void Daemon_csproj_references_kernel_and_the_two_kernel_only_open_projects_never_producers()
    {
        var csproj = File.ReadAllText(Path.Combine(DaemonSourceDir(), "LogMark.Daemon.csproj"));

        // The real invariant: Kernel plus the two open composition roots the
        // unified binary dispatches into in-process (CLI verbs, the mcp
        // verb) - these ARE expected references, not a purity violation.
        Assert.Contains("LogMark.Kernel.csproj", csproj);
        Assert.Contains("LogMark.CLI.csproj", csproj);
        Assert.Contains("LogMark.Mcp.csproj", csproj);

        // Never the closed application layer or its producer-registration
        // assemblies. ".Producers" catches any of them (LogMark.CLI.Producers,
        // LogMark.Mcp.Producers, ...) in one assertion rather than enumerating
        // each by name, so a new closed producer project added later is
        // caught automatically instead of needing this test updated too.
        Assert.DoesNotContain("LogMark.Core.csproj", csproj);
        Assert.DoesNotContain("LogMark.App.csproj", csproj);
        Assert.DoesNotContain(".Producers", csproj);
        Assert.DoesNotContain("Shr" + "oud", csproj);
        Assert.DoesNotContain("On" + "nx" + "Runtime", csproj);
    }

    [Fact]
    public void Daemon_sources_do_not_touch_closed_types()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(DaemonSourceDir(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }
            var source = File.ReadAllText(file);
            foreach (var token in ForbiddenSourceTokens)
            {
                if (source.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {token}");
                }
            }
        }
        Assert.True(offenders.Count == 0,
            "Daemon sources reference closed code:\n" + string.Join("\n", offenders));
    }

    private static string DaemonSourceDir()
    {
        // Walk up from the test bin dir to src/, then into LogMark.Daemon.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Name != "src")
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "LogMark.Daemon");
    }
}
