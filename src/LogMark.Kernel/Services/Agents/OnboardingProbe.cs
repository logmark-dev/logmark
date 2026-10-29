namespace LogMark.Services.Agents;

public record ProbeResult(
    bool CloudReachable,
    bool ClaudeCliPresent,
    bool OllamaPresent,
    bool LmStudioPresent,
    bool Registered,
    bool CodexCliPresent = false,
    string? OllamaDefaultModel = null,
    string? LmStudioDefaultModel = null)
{
    public bool HasLocalInference => ClaudeCliPresent || CodexCliPresent || OllamaPresent || LmStudioPresent;
}

public class OnboardingProbe
{
    private readonly Func<Task<bool>> _cloudChecker;
    private readonly Func<Task<bool>> _cliChecker;
    private readonly Func<Task<(bool Present, string? Model)>> _ollamaChecker;
    private readonly Func<Task<(bool Present, string? Model)>> _lmStudioChecker;
    private readonly Func<bool> _registrationChecker;
    private readonly Func<Task<bool>> _codexCliChecker;

    public OnboardingProbe(
        Func<Task<bool>> cloudChecker,
        Func<Task<bool>> cliChecker,
        Func<Task<(bool Present, string? Model)>> ollamaChecker,
        Func<Task<(bool Present, string? Model)>> lmStudioChecker,
        Func<bool> registrationChecker,
        Func<Task<bool>>? codexCliChecker = null)
    {
        _cloudChecker = cloudChecker;
        _cliChecker = cliChecker;
        _ollamaChecker = ollamaChecker;
        _lmStudioChecker = lmStudioChecker;
        _registrationChecker = registrationChecker;
        _codexCliChecker = codexCliChecker ?? (() => Task.FromResult(false));
    }

    /// <summary>
    /// Per-checker budget. Slightly above CliAvailabilityProbe's own 5s
    /// process timeout so a cold CLI binary resolves on its own terms rather
    /// than being cut off a moment early and reported absent.
    /// </summary>
    internal static readonly TimeSpan CheckerBudget = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Runs every checker concurrently and independently.
    ///
    /// INDEPENDENTLY is the whole point. This used to be a bare
    /// Task.WhenAll whose result the caller wrapped in a single 8s timeout,
    /// which made the probe all-or-nothing: one slow or throwing checker
    /// discarded every other checker's finished answer, and the caller's catch
    /// substituted a ProbeResult with every provider false. That is not a
    /// hypothetical - 0.20.0 QA on rog-dev logged "Onboarding probe failed: The
    /// operation has timed out" and showed "No AI providers detected" while
    /// Ollama was confirmed serving two models on 11434 (lmk-3o4k). The Ollama
    /// probe had in fact succeeded in about two seconds; its result was thrown
    /// away along with everything else because a sibling checker was slow.
    ///
    /// Two CLI checkers each spawn "&lt;binary&gt; --version" and wait up to 5s
    /// (CliAvailabilityProbe), so on a cold machine - first launch after boot,
    /// antivirus scanning a fresh install, exactly first-run conditions - the
    /// aggregate is well placed to exceed an 8s budget while the cheap HTTP
    /// probes have long since answered.
    ///
    /// So each checker now falls back to its own absent-value on timeout or
    /// exception, and the aggregate always reports what actually resolved. The
    /// caller's outer timeout stays as a backstop; it should no longer fire.
    /// </summary>
    public async Task<ProbeResult> RunAsync()
    {
        var cloudTask = Bounded(_cloudChecker, false);
        var cliTask = Bounded(_cliChecker, false);
        var codexCliTask = Bounded(_codexCliChecker, false);
        var ollamaTask = Bounded(_ollamaChecker, (false, (string?)null));
        var lmStudioTask = Bounded(_lmStudioChecker, (false, (string?)null));

        await Task.WhenAll(cloudTask, cliTask, codexCliTask, ollamaTask, lmStudioTask);

        return new ProbeResult(
            CloudReachable: cloudTask.Result,
            ClaudeCliPresent: cliTask.Result,
            OllamaPresent: ollamaTask.Result.Item1,
            LmStudioPresent: lmStudioTask.Result.Item1,
            Registered: SafeRegistration(),
            CodexCliPresent: codexCliTask.Result,
            OllamaDefaultModel: ollamaTask.Result.Item2,
            LmStudioDefaultModel: lmStudioTask.Result.Item2);
    }

    /// <summary>
    /// Starts a checker and guarantees it settles: its own value if it answers
    /// inside <see cref="CheckerBudget"/>, otherwise <paramref name="absent"/>.
    /// A checker that throws is also absent rather than fatal - under the old
    /// Task.WhenAll a single faulted checker threw on await and took the whole
    /// probe with it, the same failure as a slow one by a different route.
    ///
    /// The abandoned task is deliberately left running rather than cancelled:
    /// these checkers already own their internal deadlines (CliAvailabilityProbe
    /// kills its child process, LocalLlmDetector cancels its HTTP request), and
    /// nothing here can shorten those. Observe its exception so an unobserved
    /// faulted task cannot surface later on the finalizer thread.
    /// </summary>
    private static async Task<T> Bounded<T>(Func<Task<T>> checker, T absent)
    {
        Task<T> task;
        try { task = checker(); }
        catch { return absent; }
        if (task is null) return absent;

        var settled = await Task.WhenAny(task, Task.Delay(CheckerBudget)).ConfigureAwait(false);
        if (settled != task)
        {
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return absent;
        }

        try { return await task.ConfigureAwait(false); }
        catch { return absent; }
    }

    // Sync, but it reads _registrationService state through a caller-supplied
    // closure, so it is no safer than the rest. A throw here would have taken
    // the whole ProbeResult construction with it.
    private bool SafeRegistration()
    {
        try { return _registrationChecker(); }
        catch { return false; }
    }
}
