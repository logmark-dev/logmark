using System.Text;

namespace LogMark.Services.Alignment;

/// <summary>
/// The ONE selection of learned traits that reach the session-distill prompt
/// (alignment-loop-v1 design sec 5.2; final review F4). DistillPromptBlocks
/// renders exactly this selection and LearnedTraitCapLint compares it with the
/// confirmed set, so the prompt and the lint cannot disagree about which
/// traits are dropped. Lives in Kernel because the lint rule cannot call Core.
///
/// The block text (header, item shape) is part of the measured distill prompt:
/// changing it is a prompt change - replay before trusting it.
/// </summary>
public static class TraitPromptSelection
{
    public const string Scope = "session-distill";

    /// <summary>Learned traits budget: ~600 tokens, header included.</summary>
    public const int TraitBlockCharBudget = 2400;
    public const int MaxExamplesPerTrait = 2;

    public static string Header { get; } =
        "LEARNED PREFERENCES. The user approved these from their own past rejections. The vault" + Environment.NewLine +
        "constitution above outranks them." + Environment.NewLine;

    public static string Footer { get; } = "END LEARNED PREFERENCES." + Environment.NewLine + Environment.NewLine;

    /// <summary>One trait as it appears in the block: the rule plus at most two neutralized examples.</summary>
    public static string RenderItem(LearnedTrait t)
    {
        var item = new StringBuilder();
        item.Append("  - ").AppendLine(t.Rule);
        var examples = t.Examples.Take(MaxExamplesPerTrait).Select(e => "\"" + TraitFormat.NeutralizeExample(e) + "\"").ToList();
        if (examples.Count > 0) item.Append("    Do not extract entries like: ").AppendLine(string.Join("; ", examples));
        return item.ToString();
    }

    /// <summary>
    /// Confirmed session-distill traits, most evidence first, at most
    /// TraitFormat.PromptCap, fitting TraitBlockCharBudget. An item that does
    /// not fit is skipped and smaller ones are still tried, so one oversize
    /// trait cannot blank the block.
    /// </summary>
    public static IReadOnlyList<LearnedTrait> Select(IReadOnlyList<LearnedTrait>? traits)
    {
        if (traits is null || traits.Count == 0) return Array.Empty<LearnedTrait>();
        var candidates = traits
            .Where(t => t.Status == TraitStatus.Confirmed && string.Equals(t.Scope, Scope, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.EvidenceCount).ThenBy(t => t.Id, StringComparer.Ordinal);

        var selected = new List<LearnedTrait>();
        var used = Header.Length;
        foreach (var t in candidates)
        {
            if (selected.Count >= TraitFormat.PromptCap) break;
            var len = RenderItem(t).Length;
            if (used + len > TraitBlockCharBudget) continue;
            selected.Add(t);
            used += len;
        }
        return selected;
    }
}
