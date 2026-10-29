using System.Text;
using System.Text.Json;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Prompts;
using LogMark.Mcp.Tools;

namespace LogMark.Mcp;

/// <summary>
/// MCP Server with STDIO transport and JSON-RPC 2.0 protocol.
/// Uses raw streams for reliable subprocess communication.
/// </summary>
public class McpServer
{
    private readonly ToolRegistry _toolRegistry;
    private readonly PromptRegistry? _promptRegistry;
    private readonly IMcpServerHooks? _hooks;
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _initialized;

    public SemaphoreSlim SwitchSemaphore { get; } = new(1, 1);

    /// <summary>
    /// Awaited before every tool executes. The host wires it to EntryCache
    /// warmup so a call made right after initialize never reads a partial
    /// cache (bead lmk-fjcj: vault_summary reported 0 of 68 entries).
    /// </summary>
    public Func<CancellationToken, Task>? BeforeToolCall { get; set; }

    /// <summary>Product title reported in initialize serverInfo.title; null omits it.</summary>
    public string? ServerTitle { get; set; }

    public McpServer(ToolRegistry toolRegistry, PromptRegistry? promptRegistry = null, IMcpServerHooks? hooks = null, Stream? input = null, Stream? output = null)
    {
        _toolRegistry = toolRegistry;
        _promptRegistry = promptRegistry;
        _hooks = hooks;
        _input = input ?? Console.OpenStandardInput();
        _output = output ?? Console.OpenStandardOutput();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var message = await ReadMessageAsync(ct);
                if (message == null)
                    break; // EOF

                var request = JsonSerializer.Deserialize<JsonRpcRequest>(message, _jsonOptions);
                if (request == null)
                {
                    await WriteResponseAsync(JsonRpcResponse.ParseError(), ct);
                    continue;
                }

                var response = await HandleRequestAsync(request, ct);

                // Don't respond to notifications
                if (!request.IsNotification && response != null)
                {
                    await WriteResponseAsync(response, ct);
                }
            }
            catch (JsonException)
            {
                await WriteResponseAsync(JsonRpcResponse.ParseError(), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(JsonRpcResponse.InternalError(null, ex.Message), ct);
            }
        }
    }

    private async Task<string?> ReadMessageAsync(CancellationToken ct)
    {
        // MCP STDIO transport uses newline-delimited JSON (one JSON object per line).
        //
        // We accumulate raw bytes until a newline is seen, then decode the buffer as UTF-8.
        // Earlier code cast each byte directly to char, which corrupted every multi-byte
        // UTF-8 sequence (em-dashes, smart quotes, Cyrillic, CJK, emoji) into Latin-1 mojibake.
        // Setting Console.InputEncoding = UTF8 in Program.cs is not sufficient on its own
        // because Console.OpenStandardInput() returns a raw byte Stream that bypasses the
        // InputEncoding setting.
        var byteBuffer = new List<byte>(256);

        Console.Error.WriteLine("logmark-mcp: Waiting for input...");
        Console.Error.Flush();

        while (true)
        {
            var buffer = new byte[1];
            var read = await _input.ReadAsync(buffer, 0, 1, ct);
            if (read == 0)
            {
                Console.Error.WriteLine("logmark-mcp: EOF received");
                Console.Error.Flush();
                return null; // EOF
            }

            var b = buffer[0];

            // Newline marks end of message
            if (b == (byte)'\n')
            {
                if (byteBuffer.Count == 0)
                    continue; // Empty line, keep reading

                var line = Encoding.UTF8.GetString(byteBuffer.ToArray()).Trim();
                byteBuffer.Clear();
                if (!string.IsNullOrEmpty(line))
                {
                    Console.Error.WriteLine($"logmark-mcp: Received message ({line.Length} chars)");
                    Console.Error.Flush();
                    return line;
                }
                continue;
            }

            // Skip carriage returns
            if (b == (byte)'\r')
                continue;

            byteBuffer.Add(b);
        }
    }

    private async Task WriteResponseAsync(JsonRpcResponse response, CancellationToken ct)
    {
        // MCP STDIO transport uses newline-delimited JSON
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        var messageBytes = Encoding.UTF8.GetBytes(json + "\n");

        Console.Error.WriteLine($"logmark-mcp: Sending response ({json.Length} chars)");
        Console.Error.Flush();

        await _output.WriteAsync(messageBytes, 0, messageBytes.Length, ct);
        await _output.FlushAsync(ct);
    }

    private async Task<JsonRpcResponse?> HandleRequestAsync(JsonRpcRequest request, CancellationToken ct)
    {
        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "initialized" => null, // Notification, no response
            "notifications/initialized" => null, // Alternative form
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolCallAsync(request, ct),
            "prompts/list" => HandlePromptsList(request),
            "prompts/get" => await HandlePromptsGetAsync(request, ct),
            "ping" => HandlePing(request),
            _ => JsonRpcResponse.MethodNotFound(request.Id, request.Method)
        };
    }

    /// <summary>
    /// Newest first. Only versions whose initialize/tools/list/tools/call shapes
    /// this server implements. 2025-03-26 is omitted: it requires receivers to
    /// accept JSON-RPC batches, which this server does not handle.
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedProtocolVersions =
        new[] { "2025-06-18", "2024-11-05" };

    /// <summary>Echo the requested version when supported, else the newest supported one.</summary>
    public static string NegotiateProtocolVersion(string? requested) =>
        requested is not null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : SupportedProtocolVersions[0];

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        _initialized = true;

        InitializeParams? initParams = null;
        if (request.Params is { } p)
        {
            try { initParams = JsonSerializer.Deserialize<InitializeParams>(p.GetRawText(), _jsonOptions); }
            catch (JsonException) { /* malformed params: fall back to newest supported */ }
        }

        var capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability { ListChanged = false }
        };

        if (_promptRegistry != null)
        {
            capabilities.Prompts = new PromptsCapability { ListChanged = false };
        }

        var result = new InitializeResult
        {
            ProtocolVersion = NegotiateProtocolVersion(initParams?.ProtocolVersion),
            Capabilities = capabilities,
            ServerInfo = new ServerInfo
            {
                Name = "logmark-mcp",
                Version = LogMark.BuildInfo.DisplayVersion(typeof(McpServer).Assembly),
                Title = ServerTitle,
            },
            Instructions =
                "LogMark is the user's personal markdown vault: typed entries (task t:, idea i:, " +
                "block b:, decision d:, question q:, gap g:, analogy a:), organized into +routes (projects), tagged #tags. " +
                "Start with vault_summary (overview), get_project_context (one project's decisions/tasks/blocks), " +
                "or get_routes (the project list). Every result carries stable entry ids - fetch full detail " +
                "any time with read_entry(id), even after context compaction. An entry flagged Unaddressable has no " +
                $"persisted id yet: read it by path, and ask the user to run `{Program.CliName} entry mint-anchors --apply` " +
                "before changing it. Reads are safe to parallelize. " +
                "Writes (create_entry etc.) land as real markdown files the user owns.",
        };

        return JsonRpcResponse.Success(request.Id, result);
    }

    /// <summary>
    /// Tools that produce vault artifacts. Used by the post-execution telemetry
    /// hook to record "write" events for the analytics dashboard (gated by
    /// analytics opt-in in HeartbeatService).
    ///
    /// This is the write-shape predicate (tools that mutate the vault, regardless
    /// of tier). It is distinct from the closed Pro-gating set: some Pro-gated
    /// tools (e.g. execute_behavior) are NOT write-shaped -- they run inference
    /// and return results, and vault mutation happens only when the caller
    /// subsequently invokes create_entry / write_page. Some write-shaped tools
    /// (save_asset, create_view) are Pro-gated in the shipped build; the gating
    /// decision lives entirely behind IMcpServerHooks, not here.
    ///
    /// Keep in sync with new write-producing tools as they are registered.
    /// WriteShapeClassificationTests pins the open tool set against this roster,
    /// so registering an open tool without classifying it here fails the build.
    /// </summary>
    internal static readonly HashSet<string> WriteShapedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        // Open (kernel) writes
        "create_entry", "create_entry_with_subpage", "backfill_entry", "backfill_batch",
        "update_entry", "organize_entry", "write_page",
        "update_routes", "update_constitution", "update_tags",
        "start_session", "end_session",
        // Closed (desktop) writes
        "save_asset", "enrich_entry",
        "create_protocol", "update_protocol", "protocol_respond",
        // Build-gated closed writes (Debug / ShowViewsLens)
        "import_thinker_space", "create_view", "update_view"
    };

    // Centralizes the write-shape predicate. Currently a HashSet lookup;
    // reserved as the call site if ITool ever grows an IsWriteShaped property.
    private static bool IsWriteShaped(string toolName) => WriteShapedTools.Contains(toolName);

    private JsonRpcResponse HandleToolsList(JsonRpcRequest request)
    {
        if (!_initialized)
        {
            return JsonRpcResponse.Error(request.Id, -32002, "Server not initialized");
        }

        // Open registry exposes every registered tool unfiltered. Pro/closed
        // tools are simply not registered in the open build; the shipped build
        // registers them and gates execution per-call via IMcpServerHooks.
        return JsonRpcResponse.Success(request.Id, _toolRegistry.GetToolList());
    }

    private async Task<JsonRpcResponse> HandleToolCallAsync(JsonRpcRequest request, CancellationToken ct)
    {
        await SwitchSemaphore.WaitAsync(ct);
        try
        {
            if (!_initialized)
            {
                return JsonRpcResponse.Error(request.Id, -32002, "Server not initialized");
            }

            if (request.Params == null)
            {
                return JsonRpcResponse.InvalidParams(request.Id, "Missing params");
            }

            ToolCallParams? callParams;
            try
            {
                callParams = JsonSerializer.Deserialize<ToolCallParams>(request.Params.Value.GetRawText(), _jsonOptions);
            }
            catch (JsonException ex)
            {
                return JsonRpcResponse.InvalidParams(request.Id, ex.Message);
            }

            if (callParams == null || string.IsNullOrEmpty(callParams.Name))
            {
                return JsonRpcResponse.InvalidParams(request.Id, "Missing tool name");
            }

            var tool = _toolRegistry.GetTool(callParams.Name);
            if (tool == null)
            {
                return JsonRpcResponse.Error(request.Id, -32601, $"Tool not found: {callParams.Name}");
            }

            // License gating check (closed hook; open build allows all registered tools).
            if (_hooks != null)
            {
                var gatedResult = await _hooks.CheckToolAccessAsync(callParams.Name);
                if (gatedResult != null)
                {
                    return JsonRpcResponse.Success(request.Id, gatedResult);
                }
            }

            try
            {
                if (BeforeToolCall is { } beforeToolCall)
                    await beforeToolCall(ct);

                var result = await tool.ExecuteAsync(callParams.Arguments, ct);

                // Telemetry only: record writes for analytics. Never consulted
                // for gating decisions. The closed hook gates transmission on
                // analytics opt-in and zero-suppresses.
                if (_hooks != null && IsWriteShaped(callParams.Name))
                {
                    try { _hooks.RecordWrite(callParams.Name); }
                    catch { /* usage tracking is best-effort */ }
                }

                return JsonRpcResponse.Success(request.Id, result);
            }
            catch (Exception ex)
            {
                var errorResult = new ToolCallResult
                {
                    IsError = true,
                    Content = new List<ToolContent>
                    {
                        new() { Type = "text", Text = $"Error: {ex.Message}" }
                    }
                };
                return JsonRpcResponse.Success(request.Id, errorResult);
            }
        }
        finally
        {
            SwitchSemaphore.Release();
        }
    }

    private JsonRpcResponse HandlePromptsList(JsonRpcRequest request)
    {
        if (!_initialized)
            return JsonRpcResponse.Error(request.Id, -32002, "Server not initialized");

        if (_promptRegistry == null)
            return JsonRpcResponse.Success(request.Id, new PromptsListResult());

        return JsonRpcResponse.Success(request.Id, _promptRegistry.GetPromptList());
    }

    private async Task<JsonRpcResponse> HandlePromptsGetAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!_initialized)
            return JsonRpcResponse.Error(request.Id, -32002, "Server not initialized");

        if (_promptRegistry == null)
            return JsonRpcResponse.Error(request.Id, -32601, "Prompts not supported");

        if (request.Params == null)
            return JsonRpcResponse.InvalidParams(request.Id, "Missing params");

        PromptGetParams? getParams;
        try
        {
            getParams = JsonSerializer.Deserialize<PromptGetParams>(request.Params.Value.GetRawText(), _jsonOptions);
        }
        catch (JsonException ex)
        {
            return JsonRpcResponse.InvalidParams(request.Id, ex.Message);
        }

        if (getParams == null || string.IsNullOrEmpty(getParams.Name))
            return JsonRpcResponse.InvalidParams(request.Id, "Missing prompt name");

        var prompt = _promptRegistry.GetPrompt(getParams.Name);
        if (prompt == null)
            return JsonRpcResponse.Error(request.Id, -32601, $"Prompt not found: {getParams.Name}");

        try
        {
            var result = await prompt.GetAsync(getParams.Arguments, ct);
            return JsonRpcResponse.Success(request.Id, result);
        }
        catch (Exception ex)
        {
            return JsonRpcResponse.Error(request.Id, -32603, $"Prompt error: {ex.Message}");
        }
    }

    private JsonRpcResponse HandlePing(JsonRpcRequest request)
    {
        return JsonRpcResponse.Success(request.Id, new { });
    }
}
