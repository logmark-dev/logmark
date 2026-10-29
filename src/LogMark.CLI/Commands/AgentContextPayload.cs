using System.Text;

namespace LogMark.CLI.Commands;

/// <summary>
/// One titled group of already-rendered lines, with its budget share.
///
/// <see cref="Reserve"/> is a WEIGHT when the payload is rendered with
/// scaling on (the fresh shape), and an absolute character cap when it is not
/// (the continuation shape's single "In flight" section, which is deliberately
/// capped regardless of how generous the host budget is).
/// </summary>
public sealed record PayloadSection(string Title, IReadOnlyList<string> Lines, int Reserve);

/// <summary>
/// The one place the injected payload's sub-allocations are named.
///
/// The TOTAL is not here: it is <c>HookProfile.ContextBudgetChars</c>, per
/// host, and everything below is either a weight against that total or a
/// per-item clip. Before this existed the budget was decided in three
/// unrelated places (the handoff clip, the section reserves, and the
/// continuation's defensive slice) with no shared source, so a change to one
/// silently disagreed with the others - which is how the section reserves came
/// to sum to exactly the old 3000 default and cap every host at it.
/// </summary>
public static class PayloadBudget
{
    /// <summary>Held back from the fresh shape's total for its read_entry footer.</summary>
    public const int FooterReserve = 200;

    /// <summary>Longest first line of a handoff the continuation shape will print.</summary>
    public const int HandoffClipChars = 400;

    /// <summary>Longest first line of any ordinary item in any section.</summary>
    public const int ItemClipChars = 120;

    /// <summary>
    /// Absolute cap on the continuation shape's "In flight" section. NOT scaled
    /// to the host budget: a resuming agent wants "here is what is active, plus
    /// N more via get_blocks", not an ever-growing dump just because the host
    /// can afford one.
    /// </summary>
    public const int InFlightReserve = 1200;

    /// <summary>
    /// Held back for the continuation shape's pointer line. A fixed generous
    /// estimate rather than the real length, which depends on a count the
    /// render itself produces.
    /// </summary>
    public const int PointerReserve = 300;

    // Fresh-shape section weights. Relative, not absolute: blocks and tasks are
    // the highest-value working state, recent activity carries the handoff, and
    // questions/decisions/ideas are lower signal. Scaled to whatever the host's
    // ContextBudgetChars leaves after the intro and footer.
    public const int BlocksWeight = 600;
    public const int TasksWeight = 600;
    public const int QuestionsWeight = 300;
    public const int DecisionsWeight = 500;
    public const int ActivityWeight = 700;
    public const int IdeasWeight = 300;
}

/// <summary>
/// Renders the injected context under a budget.
///
/// Two rules the previous single-slice approach could not express. A section
/// may not consume budget reserved for the sections after it, so the handoff
/// can no longer be crowded out by the backlog. And a section stops at an item
/// boundary and says how many items it dropped, rather than being cut
/// mid-sentence.
/// </summary>
public static class AgentContextPayload
{
    public static string Render(IReadOnlyList<PayloadSection> sections, int totalBudget)
        => Render(sections, totalBudget, out _);

    /// <summary>
    /// Same rendering as the two-argument overload, but also reports how many
    /// items were actually rendered across all sections combined via
    /// <paramref name="shownItemCount"/>.
    ///
    /// A caller whose own downstream arithmetic depends on "how many of my
    /// candidate items did NOT make it into this payload" must ask this, not
    /// the pre-render candidate count: this method can drop further items
    /// once a section's reserve is exceeded, on top of whatever filtering the
    /// caller already applied before calling in - and that additional drop
    /// has to be counted too, or the caller's own "here's how much more there
    /// is" figure silently undercounts. (The agent-context continuation
    /// shape's pointer line is exactly this caller.)
    /// </summary>
    public static string Render(IReadOnlyList<PayloadSection> sections, int totalBudget, out int shownItemCount)
        => Render(sections, totalBudget, scaleReservesToBudget: false, out shownItemCount);

    /// <summary>
    /// <paramref name="scaleReservesToBudget"/> reads each section's Reserve as
    /// a WEIGHT and normalises the non-empty sections' shares to fill
    /// <paramref name="totalBudget"/> exactly.
    ///
    /// Without it, total rendered content can never exceed the sum of the
    /// literal reserves however generous the budget is, and every section's
    /// share is clipped proportionally when the budget is smaller than that
    /// sum - which starved the first section to about two items on the five
    /// hosts still on the 3000 default while capping Claude Code at 3000 of the
    /// 8000 it can afford. Scaling over the NON-EMPTY sections is the point:
    /// an empty section must donate its whole share rather than reserve it.
    ///
    /// Off by default, because the continuation shape's "In flight" section is
    /// deliberately capped in absolute characters (see
    /// <see cref="PayloadBudget.InFlightReserve"/>); normalising a single
    /// section would hand it the entire remaining budget.
    /// </summary>
    public static string Render(
        IReadOnlyList<PayloadSection> sections,
        int totalBudget,
        bool scaleReservesToBudget,
        out int shownItemCount)
    {
        var rendered = new List<PayloadSection>(sections.Count);
        foreach (var s in sections)
            if (s.Lines.Count > 0) rendered.Add(s);

        if (scaleReservesToBudget) ScaleReserves(rendered, totalBudget);

        var sb = new StringBuilder();
        var spent = 0;
        var carry = 0;
        var totalShown = 0;

        for (var i = 0; i < rendered.Count; i++)
        {
            var section = rendered[i];

            // Reserve for everything after this section, so a greedy early
            // section cannot spend the later sections' share.
            var downstreamReserve = 0;
            for (var j = i + 1; j < rendered.Count; j++) downstreamReserve += rendered[j].Reserve;

            var allowance = Math.Min(section.Reserve + carry, Math.Max(0, totalBudget - spent - downstreamReserve));

            // A section that renders nothing spent nothing, so its whole share
            // flows down like any other unused reserve. Discarding it here only
            // ever under-utilised the budget - it can never overrun, because
            // the allowance above is still clamped by what is left after every
            // downstream reserve.
            var header = $"\n{section.Title}:\n";
            if (allowance <= header.Length) { carry += section.Reserve; continue; }

            var body = new StringBuilder();
            var used = header.Length;
            var shown = 0;

            foreach (var line in section.Lines)
            {
                var candidate = line + "\n";
                if (used + candidate.Length > allowance) break;
                body.Append(candidate);
                used += candidate.Length;
                shown++;
            }

            if (shown == 0) { carry += section.Reserve; continue; }

            totalShown += shown;

            var dropped = section.Lines.Count - shown;
            if (dropped > 0)
            {
                var note = $"  (+{dropped} more)\n";
                if (used + note.Length <= allowance) { body.Append(note); used += note.Length; }
            }

            sb.Append(header).Append(body);
            spent += used;
            carry = Math.Max(0, section.Reserve + carry - used);
        }

        shownItemCount = totalShown;
        return sb.ToString();
    }

    /// <summary>
    /// Rewrites each section's Reserve as its proportional share of
    /// <paramref name="totalBudget"/>. The last section absorbs the integer
    /// -division remainder, so the shares sum to the budget exactly and the
    /// downstream-reserve arithmetic above stays exact.
    /// </summary>
    private static void ScaleReserves(List<PayloadSection> sections, int totalBudget)
    {
        if (sections.Count == 0 || totalBudget <= 0) return;

        var totalWeight = sections.Sum(s => (long)Math.Max(0, s.Reserve));
        if (totalWeight <= 0) return;

        var assigned = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var share = i == sections.Count - 1
                ? totalBudget - assigned
                : (int)(Math.Max(0, sections[i].Reserve) * (long)totalBudget / totalWeight);
            assigned += share;
            sections[i] = sections[i] with { Reserve = share };
        }
    }
}
