using FluentAssertions;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// Proves the open agent-capture verb (lmk-tfv4) never shadows a closed
/// registration: CommandRegistry.Register is last-wins by Name, and
/// Program.BuildRegistry always calls RegisterOpenCommands (which registers
/// the open AgentCaptureCommand) BEFORE ClosedCommandRegistrar (which, on
/// lm.exe/desktop, registers the real closed AgentCaptureCommand under the
/// same "agent-capture" name). A sentinel closed command standing in for the
/// real one is enough to prove the ordering without depending on
/// LogMark.CLI.Producers, which this project does not reference.
/// </summary>
[Collection("EnvironmentTests")]
public class AgentCaptureRegistrationOverrideTests
{
    private sealed class SentinelClosedAgentCaptureCommand : ICommand
    {
        public string Name => "agent-capture";
        public string Description => "sentinel closed agent-capture";
        public string Usage => "agent-capture";
        public bool RequiresVault => false;
        public int Execute(CliContext context, string[] args) => 0;
    }

    [Fact]
    public void ClosedRegistrar_Overrides_OpenAgentCaptureCommand()
    {
        var savedRegistrar = Program.ClosedCommandRegistrar;
        var savedFactory = Program.ContextFactory;
        try
        {
            var sentinel = new SentinelClosedAgentCaptureCommand();
            Program.ClosedCommandRegistrar = registry => registry.Register(sentinel);
            Program.ContextFactory = (vault, json, quiet) => new CliContext(vault, json, quiet);

            var registry = Program.BuildRegistry();

            registry.Find("agent-capture").Should().BeSameAs(sentinel,
                "the closed registrar runs after RegisterOpenCommands, so it must win the last-wins registration");
        }
        finally
        {
            Program.ClosedCommandRegistrar = savedRegistrar;
            Program.ContextFactory = savedFactory;
        }
    }
}
