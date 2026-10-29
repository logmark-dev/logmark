using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;
using Xunit;

namespace LogMark.Mcp.Tests;

public class ServerInstructionsTests
{
    [Fact]
    public async Task Initialize_Result_CarriesInstructions()
    {
        var registry = new ToolRegistry();
        var initRequest = CreateRequest("initialize", new { protocolVersion = "2024-11-05" });
        var (inputStream, outputStream) = CreateStreams(initRequest);
        var server = new McpServer(registry, null, null, inputStream, outputStream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        var responses = ParseResponses(outputStream);
        var json = JsonSerializer.Serialize(responses[0].Result);
        json.Should().Contain("instructions");
        json.Should().Contain("vault_summary");
        json.Should().Contain("read_entry");
    }

    [Fact]
    public async Task Instructions_NameEveryEntryTypeAndTheUnaddressableRemedy()
    {
        // rc7.2 QA I2: a vault holding gap and analogy entries met instructions
        // that listed five types, and nothing explained the Unaddressable flag.
        var registry = new ToolRegistry();
        var (inputStream, outputStream) = CreateStreams(CreateRequest("initialize", new { protocolVersion = "2024-11-05" }));
        var server = new McpServer(registry, null, null, inputStream, outputStream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        var instructions = JsonDocument.Parse(JsonSerializer.Serialize(ParseResponses(outputStream)[0].Result))
            .RootElement.GetProperty("instructions").GetString();
        instructions.Should().Contain("gap g:").And.Contain("analogy a:")
            .And.Contain("Unaddressable").And.Contain("logmark entry mint-anchors --apply");
    }

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
        var inputBytes = Encoding.UTF8.GetBytes(inputData + "\n");
        var inputStream = new MemoryStream(inputBytes);
        var outputStream = new MemoryStream();
        return (inputStream, outputStream);
    }

    private static List<JsonRpcResponse> ParseResponses(MemoryStream outputStream)
    {
        var responses = new List<JsonRpcResponse>();
        var output = Encoding.UTF8.GetString(outputStream.ToArray());

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
}
