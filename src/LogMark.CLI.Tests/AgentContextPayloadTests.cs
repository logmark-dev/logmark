using FluentAssertions;
using LogMark.CLI.Commands;
using Xunit;

namespace LogMark.CLI.Tests;

/// <summary>
/// The old builder concatenated every section then hard-sliced the result at
/// 3000 chars, so a route with ten open blocks and ten open tasks truncated
/// mid-sentence inside Open questions and never rendered recent activity - the
/// section the code's own comment calls "the session handoff that lm-checkpoint
/// writes and the agent reads back here".
/// </summary>
public class AgentContextPayloadTests
{
    private static PayloadSection Section(string title, int items, int itemChars, int reserve) =>
        new(title, Enumerable.Range(0, items).Select(i => $"- {new string('x', itemChars)}{i}").ToList(), reserve);

    [Fact]
    public void ALargeEarlySection_DoesNotStarveALaterOne()
    {
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 40, itemChars: 100, reserve: 400),
            Section("Recent activity", items: 3, itemChars: 100, reserve: 400),
        ], totalBudget: 1000);

        payload.Should().Contain("Recent activity");
    }

    [Fact]
    public void TruncationLandsOnItemBoundaries()
    {
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 40, itemChars: 100, reserve: 400),
        ], totalBudget: 1000);

        foreach (var line in payload.Split('\n').Where(l => l.StartsWith("- ")))
            line.TrimEnd().Should().MatchRegex(@"\d+$", "every rendered item is whole");
    }

    [Fact]
    public void UnusedReserveFlowsToLaterSections()
    {
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 1, itemChars: 20, reserve: 500),
            Section("Recent activity", items: 6, itemChars: 60, reserve: 200),
        ], totalBudget: 900);

        // The first section used ~25 of its 500; the second gets the remainder
        // and renders more than its own reserve would allow.
        payload.Split('\n').Count(l => l.StartsWith("- ")).Should().BeGreaterThan(4);
    }

    [Fact]
    public void ATruncatedSectionSaysHowManyItemsItDropped()
    {
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 40, itemChars: 100, reserve: 400),
        ], totalBudget: 500);

        payload.Should().MatchRegex(@"\+\d+ more");
    }

    // Pins the observable behaviour only: an empty section never appears in the
    // output. It does not distinguish which of two paths in Render produces that -
    // the upfront `Lines.Count > 0` filter, or the `shown == 0` skip inside the
    // per-section loop - since either one alone reproduces this result for a section
    // that is empty and first. Both are redundant here; see the task report.
    [Fact]
    public void EmptySectionsAreOmitted()
    {
        var payload = AgentContextPayload.Render(
        [
            new PayloadSection("Open blocks", [], 400),
            Section("Recent activity", items: 1, itemChars: 20, reserve: 400),
        ], totalBudget: 900);

        payload.Should().NotContain("Open blocks");
    }

    /// <summary>
    /// Addition beyond the brief. In ALargeEarlySection_DoesNotStarveALaterOne the
    /// first section's own Reserve (400) already sits below the downstream-adjusted
    /// ceiling (600 = totalBudget 1000 - downstream reserve 400), so the min() binds
    /// on Reserve either way and removing the downstream-reserve subtraction alone
    /// does not change that test's outcome - see the task report for the mutation
    /// evidence. This test gives the first section a Reserve equal to the whole
    /// budget, so only the downstream-reserve subtraction (not the section's own
    /// Reserve) can stop it from starving the section after it.
    /// </summary>
    /// <summary>
    /// Task 5 moved the total onto HookProfile; Task 6 replaced the
    /// proportional split with fixed absolute reserves and nobody re-scaled
    /// them. Total rendered content can never exceed the sum of the reserves,
    /// so the profile budget never bound upward for claude-code (reserves sum
    /// 3000 against a sections budget near 7760) and bound only downward for
    /// the five hosts still on the 3000 default. Scaling makes the reserves
    /// weights, so the same section proportions apply at any host budget.
    /// </summary>
    [Fact]
    public void ScaledReserves_LetAGenerousBudgetActuallyBeUsed()
    {
        var sections = new[]
        {
            Section("Open blocks", items: 20, itemChars: 100, reserve: 600),
            Section("Recent activity", items: 20, itemChars: 100, reserve: 400),
        };

        var unscaled = AgentContextPayload.Render(sections, totalBudget: 8000);
        var scaled = AgentContextPayload.Render(sections, totalBudget: 8000,
            scaleReservesToBudget: true, out _);

        // Unscaled, the 1000 of reserve is the ceiling however generous the
        // budget is - the defect this finding names.
        unscaled.Length.Should().BeLessThan(1100);
        scaled.Length.Should().BeGreaterThan(3000, "the profile budget must bind, not the reserve literals");
    }

    [Fact]
    public void ScaledReserves_StillHoldTheSectionProportions()
    {
        // 3:1 weights over a budget large enough that neither section runs out
        // of items, so what each renders is decided by its share alone.
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 60, itemChars: 100, reserve: 750),
            Section("Recent activity", items: 60, itemChars: 100, reserve: 250),
        ], totalBudget: 4000, scaleReservesToBudget: true, out _);

        var lines = payload.Split('\n');
        var blocksShown = lines.Count(l => l.StartsWith("- ") && l.Contains('x'));
        blocksShown.Should().BeGreaterThan(0);

        var activityAt = payload.IndexOf("Recent activity", StringComparison.Ordinal);
        var beforeActivity = payload[..activityAt].Split('\n').Count(l => l.StartsWith("- "));
        var afterActivity = payload[activityAt..].Split('\n').Count(l => l.StartsWith("- "));

        beforeActivity.Should().BeGreaterThan(afterActivity * 2,
            "a 750:250 weighting must still render roughly three times as much in the first section");
    }

    /// <summary>
    /// Render's two skip paths discarded carry instead of propagating it, so a
    /// section that rendered nothing at all donated nothing downstream. Proved
    /// never to overrun, only to under-utilise - and it disappears with the
    /// scaling fix above, where a skipped section's whole share would otherwise
    /// be lost.
    /// </summary>
    [Fact]
    public void ASkippedSectionsReserveFlowsDown_RatherThanBeingDiscarded()
    {
        var payload = AgentContextPayload.Render(
        [
            // One item far too long for this section's own allowance, so the
            // section renders nothing and its whole 100 is unused.
            new PayloadSection("Open blocks", [new string('x', 400)], 100),
            Section("Recent activity", items: 8, itemChars: 28, reserve: 100),
        ], totalBudget: 500);

        payload.Should().NotContain("Open blocks");
        payload.Split('\n').Count(l => l.StartsWith("- ")).Should().BeGreaterThan(3,
            "the skipped section's unused reserve must reach the section after it");
    }

    [Fact]
    public void ADownstreamReserve_IsWhatActuallyProtectsALaterSection_WhenTheEarlySectionsOwnReserveDoesNotBind()
    {
        var payload = AgentContextPayload.Render(
        [
            Section("Open blocks", items: 20, itemChars: 100, reserve: 1000),
            Section("Recent activity", items: 3, itemChars: 60, reserve: 400),
        ], totalBudget: 1000);

        payload.Should().Contain("Recent activity");
    }
}
