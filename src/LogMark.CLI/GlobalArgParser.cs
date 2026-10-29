namespace LogMark.CLI;

/// <summary>
/// Parsed global flags (before or after the command name).
/// </summary>
public record GlobalArgs(
    string? VaultPath,
    bool JsonOutput,
    bool Quiet,
    bool ShowHelp,
    bool ShowVersion,
    bool HelpJson,
    string? CommandName,
    string[] RemainingArgs);

/// <summary>
/// Global flags (`--vault`, `--json`, `--quiet`) may appear before or after the command;
/// `--help`, `--version`, `--help-json` only before.
/// </summary>
public static class GlobalArgParser
{
    public static GlobalArgs Parse(string[] args)
    {
        string? vaultPath = null;
        bool jsonOutput = false, quiet = false, showHelp = false, showVersion = false, helpJson = false;
        string? commandName = null;
        var remaining = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--vault" && i + 1 < args.Length) { vaultPath = args[++i]; continue; }
            if (arg == "--json") { jsonOutput = true; continue; }
            if (arg is "--quiet" or "-q") { quiet = true; continue; }

            if (commandName is null)
            {
                // Flags that only make sense before a command.
                if (arg is "--help" or "-h") { showHelp = true; continue; }
                if (arg is "--version" or "-v") { showVersion = true; continue; }
                if (arg == "--help-json") { helpJson = true; continue; }

                // First non-global token is the command (an unknown leading
                // flag is treated as a command name so the runner reports it).
                commandName = arg.ToLowerInvariant();
                continue;
            }

            remaining.Add(arg);
        }

        return new GlobalArgs(vaultPath, jsonOutput, quiet, showHelp, showVersion, helpJson,
            commandName, remaining.ToArray());
    }
}
