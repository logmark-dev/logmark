namespace LogMark.Services.Providers;

/// <summary>
/// User-facing names for provider ids (root design 2026-10-21 D1). Every
/// message that names the other product goes through Describe.
/// </summary>
public static class ProductNames
{
    public const string Engine = "LogMark Engine";
    public const string Desktop = "LogMark";

    /// <summary>Prose form: "LogMark Engine", "the LogMark desktop app".</summary>
    public static string Describe(string providerId) => providerId switch
    {
        "oss-cli" => Engine,
        "desktop" => "the LogMark desktop app",
        _ => $"another LogMark product ({providerId})"
    };

    /// <summary>Title form for headers and MCP serverInfo.title.</summary>
    public static string ProductFor(string providerId) => providerId switch
    {
        "oss-cli" => Engine,
        "desktop" => Desktop,
        _ => "LogMark"
    };
}
