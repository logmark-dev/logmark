namespace LogMark.Models;

/// <summary>
/// Controls how RouteService handles resolution of an unknown route name.
/// </summary>
public enum RouteCreationPolicy
{
    /// <summary>Ask the registered IRoutePromptService (today's default, interactive).</summary>
    Prompt,

    /// <summary>Create the route headlessly using the first section in routes.md as the parent.</summary>
    AutoCreate,

    /// <summary>Throw InvalidOperationException if the route does not exist.</summary>
    RejectIfUnknown
}
