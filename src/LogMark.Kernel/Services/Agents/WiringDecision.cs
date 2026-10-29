using LogMark.Services.Providers;

namespace LogMark.Services.Agents;

public enum WiringVerb { Connect, Disconnect }

public enum EntryOrigin { None, LogMark, Custom }

public enum WiringAction { Nothing, Create, AddOwner, RemoveOwner, RemoveEntry, Rewrite }

/// <summary>
/// What a caller knows about one wired entry before acting. Owners is the
/// recorded owner list, or the legacy attribution when no record exists.
/// EntryVault is the vault the entry resolves to (null when unknown).
/// WrittenByNewerVersion is set when the ownership record carries a higher
/// ownershipVersion than this LogMark knows: a newer LogMark manages it.
/// </summary>
public sealed record WiringFacts(
    EntryOrigin Origin, IReadOnlyList<string> Owners, string? EntryVault, string? WinnerProviderId,
    bool Broken = false, bool WrittenByNewerVersion = false);

public sealed record WiringOverrides(bool AllProducts = false, bool Repoint = false, bool Replace = false)
{
    public static readonly WiringOverrides None = new();
}

/// <summary>One option offered to the user. The first choice in a list is the
/// default and carries no flag; the others name the flag that selects them.</summary>
public sealed record WiringChoice(string Id, string? Flag, string Description);

public sealed record WiringDecisionResult(
    WiringAction Action, int ExitCode, string Message, IReadOnlyList<WiringChoice> Choices)
{
    public bool OffersChoice => Choices.Count > 1;
}

/// <summary>
/// The connect/disconnect contract of root design 2026-10-21 section 3.2, as
/// one pure function both products call. Defaults never destroy anything:
/// a provider removes an entry only when it is the sole owner or the caller
/// passes AllProducts.
/// </summary>
public static class WiringDecision
{
    public const int ExitChoiceNeeded = 3;

    private static readonly IReadOnlyList<WiringChoice> NoChoices = Array.Empty<WiringChoice>();

    /// <param name="servedByWinner">
    /// The entry this caller would write runs the role winner's binary (MCP:
    /// the resolver picks the most capable live provider). When the winner is
    /// another product, the caller never provisions the entry: it could not
    /// remove it afterwards, and agents would reach the winner's vault (root
    /// design 2026-10-21 D7).
    /// </param>
    public static WiringDecisionResult Decide(
        WiringVerb verb, string callerId, WiringFacts facts, string? requestedVault,
        WiringOverrides overrides, string subject, bool offerLocalScope, bool servedByWinner = false) =>
        verb == WiringVerb.Disconnect
            ? DecideDisconnect(callerId, facts, overrides, subject)
            : DecideConnect(callerId, facts, requestedVault, overrides, subject, offerLocalScope,
                servedByWinner && facts.WinnerProviderId is { } w && w != callerId ? w : null);

    private static WiringDecisionResult DecideDisconnect(
        string callerId, WiringFacts facts, WiringOverrides overrides, string subject)
    {
        if (facts.Origin == EntryOrigin.None)
            return new(WiringAction.Nothing, 0, $"{subject}: not connected", NoChoices);

        // A newer LogMark recorded rules this version cannot read; removing the
        // entry or an owner could break them, so even --all-products changes
        // nothing (final review Important 3).
        if (facts.WrittenByNewerVersion)
            return new(WiringAction.Nothing, ExitChoiceNeeded,
                $"{subject}: a newer LogMark manages this entry; nothing was removed (disconnect it with that version)",
                NoChoices);

        if (overrides.AllProducts)
            return new(WiringAction.RemoveEntry, 0, $"{subject}: removed for every LogMark product", NoChoices);

        var removeAll = new WiringChoice("all-products", "--all-products", "Remove it for every LogMark product");

        if (facts.Origin == EntryOrigin.Custom)
            return new(WiringAction.Nothing, ExitChoiceNeeded,
                $"{subject}: this entry was not written by LogMark (or the file could not be read); left as is",
                new[] { new WiringChoice("keep", null, "Leave it as is"),
                        removeAll with { Description = "Remove it anyway" } });

        var owners = facts.Owners;
        if (owners.Count == 1 && owners[0] == callerId)
            return new(WiringAction.RemoveEntry, 0, $"{subject}: removed", NoChoices);

        if (owners.Contains(callerId))
        {
            var others = Join(owners.Where(o => o != callerId));
            return new(WiringAction.RemoveOwner, 0,
                $"{subject}: still connected for {others}",
                new[] { new WiringChoice("keep", null, $"Keep it for {others}"), removeAll });
        }

        var creator = owners.Count == 0 ? "another LogMark product" : Join(owners);
        return new(WiringAction.Nothing, ExitChoiceNeeded,
            $"{subject}: connected by {creator}; {ProductNames.Describe(callerId)} did not create it, so nothing was removed",
            new[] { new WiringChoice("keep", null, "Leave it as is"), removeAll });
    }

    private static WiringDecisionResult DecideConnect(
        string callerId, WiringFacts facts, string? requestedVault, WiringOverrides overrides,
        string subject, bool offerLocalScope, string? servingProvider)
    {
        // Every new entry, in any scope, would be served by another product.
        if (servingProvider is not null) offerLocalScope = false;

        if (facts.Origin == EntryOrigin.None || (facts.Origin == EntryOrigin.Custom && overrides.Replace))
        {
            if (servingProvider is not null)
                return new(WiringAction.Nothing, ExitChoiceNeeded,
                    $"{subject}: not created; on this machine agents reach LogMark through {ProductNames.Describe(servingProvider)}, "
                    + $"so connect them from there. {ProductNames.Describe(callerId)} connects agents only where "
                    + $"{ProductNames.Describe(servingProvider)} is not installed",
                    NoChoices);
            if (facts.Origin == EntryOrigin.None)
                return new(WiringAction.Create, 0, $"{subject}: will be created", NoChoices);
        }

        if (facts.Origin == EntryOrigin.Custom)
        {
            if (overrides.Replace)
                return new(WiringAction.Rewrite, 0, $"{subject}: replacing an entry LogMark did not write", NoChoices);
            return new(WiringAction.Nothing, ExitChoiceNeeded,
                $"{subject}: an entry named logmark exists that was not written by LogMark (or the file could not be read); left as is",
                new[] { new WiringChoice("keep", null, "Leave it as is"),
                        new WiringChoice("replace", "--replace", "Replace it with a LogMark entry") });
        }

        if (facts.Broken)
            return new(WiringAction.Rewrite, 0, $"{subject}: repairing a broken entry", NoChoices);

        if (requestedVault is not null && facts.EntryVault is not null
            && !SameVault(requestedVault, facts.EntryVault))
        {
            if (overrides.Repoint)
                return new(WiringAction.Rewrite, 0,
                    $"{subject}: now points at {requestedVault} for every LogMark product", NoChoices);

            // --repoint moves every product's agents, not just the caller's
            // (bead lmk-rv41): name the products whose agents it would move.
            var moved = facts.Owners.Where(o => o != callerId).ToList();
            if (servingProvider is not null && !moved.Contains(servingProvider)) moved.Insert(0, servingProvider);
            var repoint = $"Point the shared entry at {requestedVault} for every LogMark product";
            if (moved.Count > 0) repoint += $"; agents of {Join(moved)} move off {facts.EntryVault} too";

            var choices = new List<WiringChoice> { new("keep", null, $"Keep using {facts.EntryVault}") };
            if (offerLocalScope)
                choices.Add(new("scope-local", "--scope local", $"Use {requestedVault} for this project only"));
            choices.Add(new("repoint", "--repoint", repoint));
            var mismatch = $"{subject}: agents already use {facts.EntryVault} through this entry; LogMark agents share one LogMark memory";
            if (servingProvider is not null)
                mismatch += $". To use {requestedVault} without moving them, run {ProductNames.Describe(callerId)} "
                    + "against it directly (serve --no-activevault, and CLI commands with --vault)";
            return new(WiringAction.Nothing, ExitChoiceNeeded, mismatch, choices);
        }

        if (facts.Owners.Contains(callerId))
            return new(WiringAction.Nothing, 0, $"{subject}: already connected", NoChoices);

        var winner = facts.WinnerProviderId;
        var message = winner is null || winner == callerId
            ? $"{subject}: already connected"
            : $"{subject}: already connected through {ProductNames.Describe(winner)}"
              + (facts.EntryVault is null ? "" : $" (vault {facts.EntryVault})")
              + $"; {ProductNames.Describe(callerId)} is registered as a fallback";
        return new(WiringAction.AddOwner, 0, message, NoChoices);
    }

    public static bool SameVault(string a, string b)
    {
        static string Norm(string p) => Path.GetFullPath(p).TrimEnd('\\', '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Norm(a), Norm(b), comparison);
    }

    private static string Join(IEnumerable<string> providerIds) =>
        string.Join(" and ", providerIds.Select(ProductNames.Describe));
}
