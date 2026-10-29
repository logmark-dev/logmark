using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;
using Xunit;

namespace LogMark.Mcp.Tests;

public class McpServerTests
{
    [Fact]
    public async Task Server_Initialize_ReturnsCapabilities()
    {
        // Arrange
        var request = CreateRequest("initialize", new { protocolVersion = "2024-11-05" });
        var (inputStream, outputStream) = CreateStreams(request);
        var server = new McpServer(new ToolRegistry(), null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var response = ParseResponse(outputStream);
        response.Result.Should().NotBeNull();

        var result = JsonSerializer.Deserialize<InitializeResult>(
            JsonSerializer.Serialize(response.Result));
        result!.ProtocolVersion.Should().Be("2024-11-05");
        result.ServerInfo.Name.Should().Be("logmark-mcp");
        result.ServerInfo.Version.Should().Be(LogMark.BuildInfo.DisplayVersion(typeof(McpServer).Assembly));
        result.Capabilities.Tools.Should().NotBeNull();
    }

    [Fact]
    public async Task Server_ToolsList_ReturnsRegisteredTools()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.Register(new TestTool("test_tool", "Test description"));

        var initRequest = CreateRequest("initialize", new { protocolVersion = "2024-11-05" });
        var listRequest = CreateRequest("tools/list", null, id: 2);

        var (inputStream, outputStream) = CreateStreams(initRequest + "\n" + listRequest);
        var server = new McpServer(registry, null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var responses = ParseResponses(outputStream);
        responses.Should().HaveCount(2);

        var listResponse = responses[1];
        var result = JsonSerializer.Deserialize<ToolsListResult>(
            JsonSerializer.Serialize(listResponse.Result));

        result!.Tools.Should().HaveCount(1);
        result.Tools[0].Name.Should().Be("test_tool");
    }

    [Fact]
    public async Task Server_ToolsCall_ExecutesTool()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.Register(new TestTool("echo", "Echo back input"));

        var initRequest = CreateRequest("initialize", new { protocolVersion = "2024-11-05" });
        var callRequest = CreateRequest("tools/call", new { name = "echo", arguments = new { message = "hello" } }, id: 2);

        var (inputStream, outputStream) = CreateStreams(initRequest + "\n" + callRequest);
        var server = new McpServer(registry, null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var responses = ParseResponses(outputStream);
        responses.Should().HaveCount(2);

        var callResponse = responses[1];
        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(callResponse.Result));

        result!.Content.Should().HaveCount(1);
        result.Content[0].Text.Should().Contain("hello");
    }

    [Fact]
    public async Task Server_ToolsCall_UnknownTool_ReturnsError()
    {
        // Arrange
        var initRequest = CreateRequest("initialize", new { protocolVersion = "2024-11-05" });
        var callRequest = CreateRequest("tools/call", new { name = "nonexistent" }, id: 2);

        var (inputStream, outputStream) = CreateStreams(initRequest + "\n" + callRequest);
        var server = new McpServer(new ToolRegistry(), null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var responses = ParseResponses(outputStream);
        var callResponse = responses[1];
        callResponse.ErrorData.Should().NotBeNull();
        callResponse.ErrorData!.Code.Should().Be(-32601);
    }

    [Fact]
    public async Task Server_BeforeInitialize_ReturnsError()
    {
        // Arrange - Skip initialization
        var listRequest = CreateRequest("tools/list", null);
        var (inputStream, outputStream) = CreateStreams(listRequest);
        var server = new McpServer(new ToolRegistry(), null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var response = ParseResponse(outputStream);
        response.ErrorData.Should().NotBeNull();
        response.ErrorData!.Code.Should().Be(-32002);
    }

    [Fact]
    public async Task Server_Ping_ReturnsPong()
    {
        // Arrange
        var request = CreateRequest("ping", null);
        var (inputStream, outputStream) = CreateStreams(request);
        var server = new McpServer(new ToolRegistry(), null, null, inputStream, outputStream);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        // Assert
        var response = ParseResponse(outputStream);
        response.Result.Should().NotBeNull();
    }

    // NDJSON format: one JSON object per line, no Content-Length headers
    private static string CreateRequest(string method, object? parameters, int id = 1)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = id,
            method = method,
            @params = parameters
        };
        return JsonSerializer.Serialize(request);
    }

    private static (MemoryStream input, MemoryStream output) CreateStreams(string inputData)
    {
        // Input stream with request(s), ending with newline
        var inputBytes = Encoding.UTF8.GetBytes(inputData + "\n");
        var inputStream = new MemoryStream(inputBytes);
        var outputStream = new MemoryStream();
        return (inputStream, outputStream);
    }

    private static JsonRpcResponse ParseResponse(MemoryStream outputStream)
    {
        var responses = ParseResponses(outputStream);
        return responses.First();
    }

    private static List<JsonRpcResponse> ParseResponses(MemoryStream outputStream)
    {
        var responses = new List<JsonRpcResponse>();
        var output = Encoding.UTF8.GetString(outputStream.ToArray());

        // NDJSON: one response per line
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            try
            {
                var response = JsonSerializer.Deserialize<JsonRpcResponse>(trimmed, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (response != null)
                    responses.Add(response);
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return responses;
    }

    private class TestTool : ITool
    {
        public string Name { get; }
        public ToolDefinition Definition { get; }

        public TestTool(string name, string description)
        {
            Name = name;
            Definition = new ToolDefinition
            {
                Name = name,
                Description = description,
                InputSchema = new JsonSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, JsonSchemaProperty>
                    {
                        ["message"] = new() { Type = "string", Description = "Input message" }
                    }
                }
            };
        }

        public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
        {
            var message = "no message";
            if (arguments?.TryGetValue("message", out var msgObj) == true && msgObj is JsonElement elem)
            {
                message = elem.GetString() ?? "null";
            }

            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ToolContent>
                {
                    new() { Type = "text", Text = $"Echo: {message}" }
                }
            });
        }
    }

}
