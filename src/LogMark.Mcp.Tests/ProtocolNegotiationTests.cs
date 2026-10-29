using FluentAssertions;
using Xunit;

namespace LogMark.Mcp.Tests;

// Supported versions were checked against the MCP changelogs (2026-10-15) for a
// stdio, tools-only server:
//   2024-11-05  baseline.
//   2025-06-18  removes JSON-RPC batching; structured tool output, elicitation,
//               resource links and `title` are optional. No required change to
//               initialize / tools/list / tools/call. Supported.
//   2025-03-26  EXCLUDED. It adds JSON-RPC batching, which receivers must
//               accept, and McpServer parses one request per line and has no
//               batch (array) handling. Re-add only after batch support lands.
//   2025-11-25  not verified against its changelog; not claimed.
public class ProtocolNegotiationTests
{
    [Theory]
    [InlineData("2024-11-05", "2024-11-05")]
    [InlineData("2025-06-18", "2025-06-18")]
    public void Supported_request_is_echoed(string requested, string expected)
    {
        McpServer.NegotiateProtocolVersion(requested).Should().Be(expected);
    }

    [Theory]
    [InlineData("2099-01-01")]
    [InlineData("2025-03-26")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsupported_request_falls_back_to_newest_supported(string? requested)
    {
        McpServer.NegotiateProtocolVersion(requested).Should().Be(McpServer.SupportedProtocolVersions[0]);
    }

    [Fact]
    public void Supported_list_excludes_unverified_versions()
    {
        McpServer.SupportedProtocolVersions.Should().NotContain("2025-03-26").And.NotContain("2025-11-25");
    }
}
