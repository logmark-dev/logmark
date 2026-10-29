using System.Runtime.CompilerServices;

namespace LogMark.CLI.Tests;

internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        FluentAssertions.License.Accepted = true;

        // Open substrate only: this project references LogMark.CLI (Kernel-only)
        // and carries no closed-verb wiring. Program.ClosedCommandRegistrar and
        // Program.ContextFactory are left null, so Program.Main registers the
        // open kernel verbs alone. Tests that drive closed verbs (feed, asset,
        // thinktank, concepts, dream, agent-capture) live in
        // LogMark.CLI.Producers.Tests, whose own ModuleInit wires the closed
        // registrar the same way the shipped `lm` entry point does.
    }
}
