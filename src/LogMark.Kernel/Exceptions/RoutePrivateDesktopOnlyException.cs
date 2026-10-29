namespace LogMark.Exceptions;

/// <summary>
/// Thrown when a non-UI surface (MCP, CLI, Telegram relay) attempts to write to
/// a route marked private. The surface's host is expected to catch this and translate
/// to a user-facing error ("use the desktop app"). See design doc Section 6d.
/// </summary>
public class RoutePrivateDesktopOnlyException : Exception
{
    public const string ErrorCode = "ROUTE_PRIVATE_DESKTOP_ONLY";

    public string RouteName { get; }

    public RoutePrivateDesktopOnlyException(string routeName)
        : base($"Route '{routeName}' is private. Captures must go through the desktop app.")
    {
        RouteName = routeName;
    }
}
