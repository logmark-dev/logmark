using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

public class JsonMcpServersWriter : IAgentConfigWriter
{
    public virtual void WriteLogmarkEntry(string configPath, string configKey,
        string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        var root = ReadOrCreateRoot(configPath);

        if (root[configKey] is not JsonObject servers)
        {
            servers = new JsonObject();
            root[configKey] = servers;
        }

        servers["logmark"] = BuildLogmarkEntry(mcpExePath, bootstrapPath, agentSlug, argsPrefix);

        WriteWithBackup(configPath, root);
    }

    public virtual void RemoveLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath))
            return;

        var root = TryReadRootObject(configPath);
        if (root == null)
            return;

        if (root[configKey] is JsonObject servers)
            servers.Remove("logmark");

        WriteWithBackup(configPath, root);
    }

    public virtual bool HasLogmarkEntry(string configPath, string configKey)
    {
        if (!File.Exists(configPath))
            return false;

        string json;
        try { json = File.ReadAllText(configPath); }
        catch { return false; }
        return HasLogmarkEntryInText(json, configKey);
    }

    /// <summary>
    /// The detection rule behind <see cref="HasLogmarkEntry"/>, over config
    /// text the caller has already read (and, for ClaudeUserScopeWriter,
    /// narrowed to the user scope).
    /// </summary>
    protected static bool HasLogmarkEntryInText(string json, string configKey)
    {
        try
        {
            // Detect by command path: any LogMark MCP wiring invokes the
            // logmark-mcp binary, regardless of what key the user chose for
            // the entry (LogMark writes "logmark", external tooling like
            // `claude mcp add logmark-mcp ...` writes whatever the user typed).
            // The binary name is unique enough to use as a substring marker.
            if (json.Contains("logmark-mcp", StringComparison.OrdinalIgnoreCase))
                return true;

            // Fallback: canonical "logmark" key for backward compat.
            using var doc = JsonDocument.Parse(json, ParseOptions);
            if (doc.RootElement.TryGetProperty(configKey, out var servers)
                && servers.ValueKind == JsonValueKind.Object
                && servers.TryGetProperty("logmark", out _))
                return true;
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Null: the flat-file writers leave entry isolation to InboundWirer's
    /// text scoping. Subclasses that nest the entry somewhere text scoping
    /// cannot find unambiguously (ClaudeLocalScopeWriter) return it here.
    /// </summary>
    public virtual string? ReadLogmarkEntryText(string configPath, string configKey) => null;

    /// <summary>
    /// Serialization for entry text handed to InboundWirer's flag scan.
    /// Non-ASCII stays literal so flag values read back from the scoped text
    /// (vault paths, bootstrap paths) compare equal to paths on disk.
    /// </summary>
    protected static readonly JsonSerializerOptions EntryTextOptions =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    protected virtual JsonObject BuildLogmarkEntry(string mcpExePath, string bootstrapPath, string agentSlug,
        IReadOnlyList<string>? argsPrefix = null)
    {
        var args = new JsonArray();
        if (argsPrefix != null)
            foreach (var a in argsPrefix)
                args.Add(JsonValue.Create(a));
        args.Add(JsonValue.Create("--bootstrap-path"));
        args.Add(JsonValue.Create(bootstrapPath));
        args.Add(JsonValue.Create("--agent"));
        args.Add(JsonValue.Create(agentSlug));

        return new JsonObject
        {
            ["command"] = JsonValue.Create(mcpExePath),
            ["args"] = args
        };
    }

    /// <summary>
    /// The root a write starts from: a new object when the file is absent or
    /// blank, else the parsed root. See <see cref="ReadExistingRootForWrite"/>
    /// for the refusal on an unparseable file.
    /// </summary>
    protected static JsonObject ReadOrCreateRoot(string configPath)
        => ReadExistingRootForWrite(configPath) ?? new JsonObject();

    /// <summary>
    /// The existing root a write must preserve: null when the file is absent
    /// or blank (nothing to lose), else the parsed root. A file that is not
    /// valid JSON throws instead of yielding an empty root - the write would
    /// otherwise replace the whole file (for Claude Code, its live
    /// ~/.claude.json state) with just the logmark entry. InboundWirer.Connect
    /// turns the exception into a failed WiringResult. Internal so
    /// AmazonQAgentDefinitionWriter, which is not a subclass, shares it.
    /// </summary>
    protected internal static JsonObject? ReadExistingRootForWrite(string configPath)
    {
        if (!File.Exists(configPath))
            return null;

        string json;
        try { json = File.ReadAllText(configPath); }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{configPath} could not be read; not rewriting it: {ex.Message}", ex);
        }
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return TryParseRootObject(json)
            ?? throw new InvalidDataException(
                $"{configPath} is not valid JSON (comments and trailing commas are tolerated); not rewriting it.");
    }

    /// <summary>
    /// The single read policy for every JSON writer: the parsed root object,
    /// or null when the file is absent, unreadable, or not a JSON object.
    /// Reads degrade to "no entry"; writes use ReadExistingRootForWrite, which
    /// refuses instead.
    /// </summary>
    protected internal static JsonObject? TryReadRootObject(string configPath)
    {
        if (!File.Exists(configPath)) return null;
        try { return TryParseRootObject(File.ReadAllText(configPath)); }
        catch { return null; }
    }

    /// <summary>
    /// JSONC tolerance: Zed's settings.json and VS Code style mcp.json files
    /// carry comments and trailing commas. Comments are dropped on rewrite;
    /// every other key survives.
    /// </summary>
    protected static readonly JsonDocumentOptions ParseOptions =
        new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonObject? TryParseRootObject(string json)
    {
        try { return JsonNode.Parse(json, documentOptions: ParseOptions) as JsonObject; }
        catch { return null; }
    }

    protected static void WriteWithBackup(string configPath, JsonObject root)
    {
        if (File.Exists(configPath))
            File.Copy(configPath, configPath + ".bak", overwrite: true);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var output = root.ToJsonString(options);
        File.WriteAllText(configPath, output);
    }
}
