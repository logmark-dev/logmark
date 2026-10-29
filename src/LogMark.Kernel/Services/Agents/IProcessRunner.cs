namespace LogMark.Services.Agents;

public record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public interface IProcessRunner
{
    ProcessResult Run(string executable, string[] args, TimeSpan timeout);
    bool IsOnPath(string executable);
}
