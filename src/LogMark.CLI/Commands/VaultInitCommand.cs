using LogMark.Exceptions;
using LogMark.Services;

namespace LogMark.CLI.Commands;

public class VaultInitCommand : ICommand
{
    public string Name => "init";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--bundle"] = true, ["--force"] = false, ["--no-seed"] = false,
    };

    public string Description => "Initialize a new vault";
    public string Usage => "vault init [path] [--bundle=<id>] [--force] [--no-seed]";
    public bool RequiresVault => false;

    public int Execute(CliContext context, string[] args)
    {
        string? path = null;
        string bundleId = ConnectCommand.OwnerId == "oss-cli" ? "open" : "build";
        bool force = false;
        bool noSeed = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--bundle="))
            {
                bundleId = arg.Substring("--bundle=".Length);
            }
            else if (arg == "--force")
            {
                force = true;
            }
            else if (arg == "--no-seed")
            {
                noSeed = true;
            }
            else if (!arg.StartsWith("-"))
            {
                path = arg;
            }
        }

        // Positional path wins; then a global --vault (any position); then CWD.
        if (string.IsNullOrEmpty(path))
            path = context.VaultPath ?? Directory.GetCurrentDirectory();

        path = Path.GetFullPath(path);

        // Refuse before creating anything: a vault inside a vault is read by
        // the outer one as a route and its entries double-counted (lmk-zj5c).
        if (!force && VaultInitializer.FindEnclosingVault(path) is { } enclosing)
        {
            Console.Error.WriteLine($"{path} is inside an existing LogMark vault: {enclosing}");
            Console.Error.WriteLine("Choose a folder outside it, or use --force to create a nested vault anyway.");
            return 1;
        }

        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to create directory: {ex.Message}");
                return 1;
            }
        }

        try
        {
            // OwnerId already distinguishes "desktop" (lm.exe / desktop app's own
            // CLI paths) from "oss-cli" (the unified logmark binary) - same product
            // marker ConnectCommand uses for skills-pack ownership, reused here so
            // the vault stamp's seededBy tells the same story.
            var version = BuildInfo.ManifestVersion(typeof(Program).Assembly);
            var initializer = new VaultInitializer(path, force, seededBy: $"{ConnectCommand.OwnerId}/{version}");
            initializer.Initialize(bundleId, seedEntries: !noSeed);

            // The unified open binary is invoked as `logmark`; the desktop's
            // sidecar CLI as `lm`. OwnerId already encodes which composition
            // root this is (same marker the vault stamp's seededBy uses), so
            // the printed next steps name the command the user actually ran.
            var verb = ConnectCommand.OwnerId == "oss-cli" ? "logmark" : "lm";

            Console.WriteLine($"Initialized LogMark vault at: {path}");
            Console.WriteLine($"Bundle: {bundleId}{(noSeed ? " (no seed entries)" : "")} - pick another with --bundle=<id>, or --no-seed for an empty start");
            Console.WriteLine();
            Console.WriteLine("Created structure:");
            Console.WriteLine("  logmark/");
            Console.WriteLine("    _inbox/          Unrouted entries");
            Console.WriteLine("    _system/         Configuration (routes.md, tags.md, config.md)");
            foreach (var dir in Directory.GetDirectories(Path.Combine(path, "logmark"))
                         .Select(Path.GetFileName)
                         .Where(n => n != null && !n.StartsWith("_"))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                if (bundleId != "open")
                    Console.WriteLine($"    {dir + "/",-17}Route (from the {bundleId} bundle)");
            }
            if (bundleId == "open")
                Console.WriteLine("    vault.md         Your constitution - edit \"How agents should help\" to steer agents");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            if (verb == "logmark")
            {
                Console.WriteLine($"  1. Serve the vault:           logmark serve --vault \"{path}\"");
                Console.WriteLine("     then open the cockpit at   http://127.0.0.1:4747");
                Console.WriteLine($"  2. Wire an agent over MCP:    logmark connect claude-code --mcp --vault \"{path}\"");
                Console.WriteLine("     (frameworks: claude-code, codex, gemini-cli, cursor, copilot-cli)");
                Console.WriteLine($"  3. Capture your first entry:  logmark capture --vault \"{path}\" \"t: try logmark\"");
            }
            else
            {
                Console.WriteLine("  1. Edit logmark/_system/routes.md to add your routes");
                Console.WriteLine("  2. Capture your first entry:  lm capture \"t: try logmark\" (or the capture hotkey)");
            }
            Console.WriteLine();
            Console.WriteLine("The vault is plain markdown in a folder you own - Obsidian-compatible, portable, yours.");

            return 0;
        }
        catch (VaultExistsException)
        {
            Console.Error.WriteLine($"LogMark structure already exists at: {path}");
            Console.Error.WriteLine("Use --force to overwrite.");
            return 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize vault: {ex.Message}");
            return 1;
        }
    }
}
