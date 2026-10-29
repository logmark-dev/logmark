namespace LogMark.Events;

/// <summary>
/// Published when a new scope is auto-registered for a route.
/// </summary>
public record ScopeCreatedEvent(string RouteName, string ScopeName);
