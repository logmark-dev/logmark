using LogMark.Services;

namespace LogMark.TestUtilities;

/// <summary>
/// Mock prompt service for testing route creation flows.
/// </summary>
public class MockRoutePromptService : IRoutePromptService
{
    private readonly Dictionary<string, RoutePromptResult> _responses = new();
    public List<string> PromptedRoutes { get; } = new();
    public List<RoutePromptOptions> PromptedOptions { get; } = new();

    /// <summary>
    /// Set the response for a specific route name.
    /// </summary>
    public void SetResponse(string routeName, RoutePromptResult result)
    {
        _responses[routeName] = result;
    }

    /// <summary>
    /// Set a Create response for a specific route with the given path.
    /// </summary>
    public void SetCreateResponse(string routeName, string path)
    {
        _responses[routeName] = new RoutePromptResult(RoutePromptOutcome.Create, path);
    }

    /// <summary>
    /// Set a ReferenceOnly response for a specific route.
    /// </summary>
    public void SetReferenceOnlyResponse(string routeName)
    {
        _responses[routeName] = new RoutePromptResult(RoutePromptOutcome.ReferenceOnly, null);
    }

    /// <summary>
    /// Set a Cancel response for a specific route.
    /// </summary>
    public void SetCancelResponse(string routeName)
    {
        _responses[routeName] = new RoutePromptResult(RoutePromptOutcome.Cancel, null);
    }

    public Task<RoutePromptResult> PromptForRouteCreationAsync(
        string routeName,
        RoutePromptOptions options)
    {
        PromptedRoutes.Add(routeName);
        PromptedOptions.Add(options);

        var result = _responses.TryGetValue(routeName, out var r)
            ? r
            : new RoutePromptResult(RoutePromptOutcome.Cancel, null);

        return Task.FromResult(result);
    }
}
