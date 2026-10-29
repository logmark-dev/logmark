namespace LogMark.Services.Hosting;

/// <summary>
/// The explicit-registration seam between the open host and producer packages
/// (open substrate split design 4.2). Closed assemblies expose one
/// AddProducers(IProducerRegistrar) entry point each; the open daemon
/// registers nothing. No assembly scanning, ever.
///
/// Contributions are type-keyed and opaque to the kernel: the kernel stores
/// and hands them back without interpreting them, so closed contract types
/// (feed detectors, MCP tools, verbs) never cross into kernel code. The
/// consumer that owns the contract type retrieves its category via
/// ProducerHost.Contributions&lt;T&gt;().
/// </summary>
public interface IProducerRegistrar
{
    /// <summary>Register a long-running producer whose lifecycle the host owns.</summary>
    void AddBackgroundLoop(IBackgroundLoop loop);

    /// <summary>Register an opaque contribution under its contract type.</summary>
    void AddContribution<T>(T contribution) where T : notnull;
}

/// <summary>A long-running producer with a host-owned lifecycle.</summary>
public interface IBackgroundLoop
{
    string Name { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}
