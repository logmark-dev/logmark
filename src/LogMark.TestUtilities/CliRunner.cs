using LogMark.CLI;

namespace LogMark.TestUtilities;

/// <summary>
/// Result from running a CLI command.
/// </summary>
public record CliResult(int ExitCode, string StdOut, string StdErr);

/// <summary>
/// Runs CLI commands in-process for testing.
/// Captures stdout/stderr by redirecting Console streams.
/// Thread-safe: uses lock to prevent concurrent Console stream manipulation.
/// </summary>
public static class CliRunner
{
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Run a CLI command with the given arguments (no stdin).
    /// Explicitly disables stdin redirection detection to prevent
    /// test runners (which redirect Console streams) from triggering pipe reads.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Result with exit code and captured output</returns>
    public static CliResult Run(params string[] args)
    {
        lock (ConsoleLock)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;

            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            // Explicitly disable stdin detection -- test runners redirect console streams.
            // Only open-verb commands are toggled here; closed hook commands
            // (agent-capture) live in LogMark.CLI.Producers and manage their own
            // IsInputRedirected in their own tests, keeping this shared runner
            // free of any closed-project reference.
            var originalCaptureRedirected = CLI.Commands.CaptureCommand.IsInputRedirected;
            var originalPageWriteRedirected = CLI.Commands.PageWriteCommand.IsInputRedirected;
            var originalAgentContextRedirected = CLI.Commands.AgentContextCommand.IsInputRedirected;
            var originalConnectInteractive = CLI.Commands.ConnectCommand.IsInteractive;
            CLI.Commands.CaptureCommand.IsInputRedirected = () => false;
            CLI.Commands.PageWriteCommand.IsInputRedirected = () => false;
            CLI.Commands.AgentContextCommand.IsInputRedirected = () => false;
            CLI.Commands.ConnectCommand.IsInteractive = () => false;

            try
            {
                var exitCode = Program.Main(args);
                return new CliResult(exitCode, outWriter.ToString(), errWriter.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
                CLI.Commands.CaptureCommand.IsInputRedirected = originalCaptureRedirected;
                CLI.Commands.PageWriteCommand.IsInputRedirected = originalPageWriteRedirected;
                CLI.Commands.AgentContextCommand.IsInputRedirected = originalAgentContextRedirected;
                CLI.Commands.ConnectCommand.IsInteractive = originalConnectInteractive;
            }
        }
    }

    /// <summary>
    /// Run a CLI command with simulated stdin content (pipe).
    /// </summary>
    /// <param name="stdinContent">Content to feed as stdin</param>
    /// <param name="args">Command line arguments</param>
    /// <returns>Result with exit code and captured output</returns>
    public static CliResult RunWithStdin(string stdinContent, params string[] args)
    {
        lock (ConsoleLock)
        {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            var originalIn = Console.In;

            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();

            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var inReader = new StringReader(stdinContent);
            Console.SetIn(inReader);
            var originalCaptureRedirected = CLI.Commands.CaptureCommand.IsInputRedirected;
            var originalPageWriteRedirected = CLI.Commands.PageWriteCommand.IsInputRedirected;
            var originalAgentContextRedirected = CLI.Commands.AgentContextCommand.IsInputRedirected;
            var originalConnectInteractive = CLI.Commands.ConnectCommand.IsInteractive;
            CLI.Commands.CaptureCommand.IsInputRedirected = () => true;
            CLI.Commands.PageWriteCommand.IsInputRedirected = () => true;
            CLI.Commands.AgentContextCommand.IsInputRedirected = () => true;
            CLI.Commands.ConnectCommand.IsInteractive = () => false;

            try
            {
                var exitCode = Program.Main(args);
                return new CliResult(exitCode, outWriter.ToString(), errWriter.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
                Console.SetIn(originalIn);
                CLI.Commands.CaptureCommand.IsInputRedirected = originalCaptureRedirected;
                CLI.Commands.PageWriteCommand.IsInputRedirected = originalPageWriteRedirected;
                CLI.Commands.AgentContextCommand.IsInputRedirected = originalAgentContextRedirected;
                CLI.Commands.ConnectCommand.IsInteractive = originalConnectInteractive;
            }
        }
    }
}
