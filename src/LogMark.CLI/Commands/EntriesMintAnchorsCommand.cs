using LogMark.Services;

namespace LogMark.CLI.Commands;

/// <summary>
/// Scans the vault for entries the parser recognizes but that carry no
/// block anchor, and mints one so they become transcludable ([[file#^id]]).
/// Dry-run by default; --apply mints and writes.
/// </summary>
public class EntriesMintAnchorsCommand : ICommand
{
    public string Name => "mint-anchors";

    public IReadOnlyDictionary<string, bool>? KnownFlags { get; } = new Dictionary<string, bool>
    {
        ["--apply"] = false,
    };

    public string Description => "Mint block anchors for entries that lack one (dry-run unless --apply)";
    public string Usage => "entry mint-anchors [--apply]";
    public bool RequiresVault => true;

    public string[]? Examples => new[]
    {
        "lm entry mint-anchors            # preview entries lacking a block anchor",
        "lm entry mint-anchors --apply    # mint anchors and write the affected files",
    };

    public int Execute(CliContext context, string[] args)
    {
        var apply = args.Any(a => string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));

        var sweep = new AnchorMintSweep(context.VaultPath!, writer: context.VaultWriter);
        var result = apply ? sweep.Apply() : sweep.Plan();

        if (!apply)
        {
            context.WriteResult($"{result.Candidates} entries lack block anchors");
            foreach (var path in result.SamplePaths)
                context.WriteResult($"  {path}");
            return 0;
        }

        var note = result.Adopted > 0 ? $" ({result.Adopted} kept their published short_id)" : "";
        context.WriteResult($"Minted {result.Candidates} anchor(s).{note}");
        return 0;
    }
}
