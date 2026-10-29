using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Tools;
using Xunit;

namespace LogMark.Mcp.Tests;

public class McpServerTitleTests
{
    [Fact]
    public async Task Initialize_reports_the_server_title_when_set()
    {
        var request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\"}}\n";
        var input = new MemoryStream(Encoding.UTF8.GetBytes(request));
        var output = new MemoryStream();
        var server = new McpServer(new ToolRegistry(), null, null, input, output) { ServerTitle = "LogMark Engine" };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await server.RunAsync(cts.Token);

        var line = Encoding.UTF8.GetString(output.ToArray()).Split('\n')[0];
        using var doc = JsonDocument.Parse(line);
        var info = doc.RootElement.GetProperty("result").GetProperty("serverInfo");
        info.GetProperty("name").GetString().Should().Be("logmark-mcp");
        info.GetProperty("title").GetString().Should().Be("LogMark Engine");
    }

    [Fact]
    public void Open_build_title_is_the_engine()
    {
        Program.ServerTitleFor(null).Should().Be("LogMark Engine");
    }
}
