namespace LogMark.Services.Verbs;

/// <summary>Outcome of executing a verb. Message is human-facing on success.</summary>
public record VerbResult(bool Success, string? Message, string? Error)
{
    public static VerbResult Ok(string message) => new(true, message, null);
    public static VerbResult Fail(string error) => new(false, null, error);
}

/// <summary>The backing call a verb dispatches to. Bound at registration.</summary>
public delegate Task<VerbResult> VerbHandler(VerbExecutionContext ctx);

/// <summary>
/// The single Core vocabulary. Each verb is declared once with its backing
/// handler; every surface (chat, MCP, CLI, WPF) dispatches through Execute
/// (Task 3) rather than re-declaring tools.
/// </summary>
public class VerbRegistry
{
    private readonly Dictionary<string, (Verb Verb, VerbHandler Handler)> _verbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _audit;

    /// <param name="audit">
    /// Optional audit sink, invoked once per Execute with the verb's actual
    /// outcome. Wired here rather than per-tool because Execute is the single
    /// point every surface (chat, MCP, CLI, WPF) passes through: before
    /// 2026-07-27 only OrganizeEntryTool's catch block logged anything, so a
    /// grooming run that retyped 14 blocks left no trace in the vault's audit
    /// trail while the REFLECT band logged every call.
    /// </param>
    public VerbRegistry(Action<string>? audit = null) => _audit = audit;

    public void Register(Verb verb, VerbHandler handler)
    {
        if (_verbs.ContainsKey(verb.Name))
            throw new InvalidOperationException($"Verb already registered: {verb.Name}");
        _verbs[verb.Name] = (verb, handler);
    }

    public Verb? Find(string name) =>
        _verbs.TryGetValue(name, out var entry) ? entry.Verb : null;

    public VerbHandler? Handler(string name) =>
        _verbs.TryGetValue(name, out var entry) ? entry.Handler : null;

    public IReadOnlyList<Verb> ByBand(VerbBand band) =>
        _verbs.Values.Where(e => e.Verb.Band == band).Select(e => e.Verb).ToList();

    public IReadOnlyList<Verb> All() =>
        _verbs.Values.Select(e => e.Verb).ToList();

    public async Task<VerbResult> Execute(
        string name,
        IReadOnlyDictionary<string, string?> args,
        string surface,
        VerbTier callerTier)
    {
        if (!_verbs.TryGetValue(name, out var entry))
            return VerbResult.Fail($"Unknown verb: {name}");

        if (entry.Verb.Tier == VerbTier.Pro && callerTier == VerbTier.Free)
            return VerbResult.Fail($"{name} is a Pro verb; this caller is on the Free tier.");

        var ctx = new VerbExecutionContext { Args = args, Surface = surface, CallerTier = callerTier };
        var result = await entry.Handler(ctx);

        // Audit after the handler, describing what it actually did. Failures are
        // logged too: a verb that declines is exactly as interesting as one that
        // acts, and silence on both is what made the 2026-07-27 groom run
        // undiagnosable from logmark/_system/log.md.
        if (_audit is not null)
        {
            args.TryGetValue("entry_id", out var entryId);
            var target = string.IsNullOrWhiteSpace(entryId) ? "-" : entryId;
            _audit(result.Success
                ? $"{surface} {name}: {target} - {result.Message}"
                : $"{surface} {name} FAILED: {target} - {result.Error}");
        }

        return result;
    }
}
