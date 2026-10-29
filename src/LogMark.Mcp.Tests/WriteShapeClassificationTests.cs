using FluentAssertions;
using LogMark.Mcp;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Guards <see cref="McpServer.WriteShapedTools"/> against drift. The roster is
/// hand-maintained (write-shape cannot be derived from ITool), so nothing stopped
/// it from carrying names of deleted tools while missing live ones - which is
/// exactly what happened: it listed flag_entry / set_entry_rating /
/// action_feed_item / dismiss_feed_item long after those tools were removed, and
/// never gained update_constitution / update_tags / enrich_entry.
///
/// Consequence was undercounted write analytics (the roster feeds
/// IMcpServerHooks.RecordWrite only, never gating - see McpServer.HandleToolsCall).
///
/// These tests pin the OPEN tool set in both directions: every registered open
/// tool must be classified exactly once, so adding a tool without deciding its
/// write-shape fails here. Closed tools cannot be enumerated from this assembly
/// (Producer is null in these tests), so they are asserted by name only.
/// </summary>
public class WriteShapeClassificationTests
{
    /// <summary>
    /// Open tools that do NOT mutate the vault. Union with the open half of
    /// WriteShapedTools must equal the full registered open set.
    /// </summary>
    private static readonly string[] ExpectedOpenReadTools =
    {
        "list_recent", "get_routes", "search", "vault_summary", "read_entry",
        "get_decisions", "get_tasks", "get_blocks", "get_project_context",
        "get_constitution", "get_sessions",
    };

    private static string[] RegisteredOpenTools()
    {
        var savedProducer = Program.Producer;
        try
        {
            Program.Producer = null;
            return Program.BuildToolRegistry(new VaultServices())
                .GetToolList().Tools.Select(t => t.Name).ToArray();
        }
        finally
        {
            Program.Producer = savedProducer;
        }
    }

    [Fact]
    public void EveryOpenTool_IsClassified_AsExactlyOneOfReadOrWrite()
    {
        foreach (var tool in RegisteredOpenTools())
        {
            var isWrite = McpServer.WriteShapedTools.Contains(tool);
            var isRead = ExpectedOpenReadTools.Contains(tool);

            (isWrite || isRead).Should().BeTrue(
                $"open tool '{tool}' is registered but not classified - add it to " +
                "McpServer.WriteShapedTools or to ExpectedOpenReadTools");
            (isWrite && isRead).Should().BeFalse(
                $"open tool '{tool}' is classified as both read and write");
        }
    }

    [Fact]
    public void WriteShapedRoster_ContainsNoDeadToolNames()
    {
        // Every open-tool entry in the roster must still be registered. Closed
        // and build-gated tools are exempt: they are not registered here.
        var registeredOpen = RegisteredOpenTools().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownClosed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "save_asset", "enrich_entry", "create_protocol", "update_protocol",
            "protocol_respond", "import_thinker_space", "create_view", "update_view",
        };

        var dead = McpServer.WriteShapedTools
            .Where(t => !registeredOpen.Contains(t) && !knownClosed.Contains(t))
            .ToList();

        dead.Should().BeEmpty(
            "WriteShapedTools names tools that no longer exist; remove them or, if a " +
            "new closed tool was added, list it in knownClosed here");
    }

    [Fact]
    public void OpenWriteTools_CoverTheMutatingKernelSurface()
    {
        // Explicit floor: these mutate the vault and must always be recorded as
        // writes. update_constitution / update_tags were the ones missing.
        var mustBeWrites = new[]
        {
            "create_entry", "create_entry_with_subpage", "backfill_entry",
            "backfill_batch", "update_entry", "organize_entry", "write_page",
            "update_routes", "update_constitution", "update_tags",
            "start_session", "end_session",
        };

        McpServer.WriteShapedTools.Should().Contain(mustBeWrites);
    }

    [Fact]
    public void ClosedWriteTools_AreClassified()
    {
        // enrich_entry writes a subpage via VaultActionRunner -> SubpageWriter.
        // execute_behavior is deliberately absent: it runs inference and returns
        // a result; mutation only happens if the caller then calls create_entry.
        McpServer.WriteShapedTools.Should().Contain(new[]
        {
            "save_asset", "enrich_entry", "create_protocol", "update_protocol",
            "protocol_respond",
        });

        McpServer.WriteShapedTools.Should().NotContain("execute_behavior");
        McpServer.WriteShapedTools.Should().NotContain("get_agent_scorecard");
        McpServer.WriteShapedTools.Should().NotContain("get_behaviors");
        McpServer.WriteShapedTools.Should().NotContain("get_protocols");
    }
}
