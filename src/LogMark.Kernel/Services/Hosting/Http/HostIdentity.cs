namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Which product is serving the v0 API, so the cockpit can name itself
/// (root design 2026-10-21 section 4.1). Additive field on /capabilities.
/// </summary>
public sealed record HostIdentity(string ProviderId, string Product, bool HasConceptProducer = false)
{
    public static HostIdentity Engine { get; } = new("oss-cli", LogMark.Services.Providers.ProductNames.Engine);

    /// <summary>The desktop runs the closed dream loop, the only writer of
    /// concept pages; the cockpit hides concept surfaces elsewhere (lmk-hqgm).</summary>
    public static HostIdentity Desktop { get; } = new("desktop", LogMark.Services.Providers.ProductNames.Desktop, HasConceptProducer: true);
}
