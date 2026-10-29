using FluentAssertions;
using LogMark.Mcp;
using Xunit;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Guards the open-build shape: with <see cref="Program.Producer"/> null (the
/// state of the mirrored OSS build, or the open LogMark.Mcp project on its own
/// with the closed LogMark.Mcp.Producers project simply absent), the tool
/// registry must contain exactly the kernel tool set and none of the Pro/closed
/// tools. This pinned list is the Task 4 Step 3 conformance baseline for the open
/// MCP surface.
///
/// LogMark.Mcp.Tests never sets Producer (no ModuleInit does), so the default
/// state here is already the open shape; the test snapshots and restores it in a
/// try/finally for determinism regardless.
/// </summary>
public class OpenBuildContractTests
{
    /// <summary>
    /// The open MCP kernel tool set: ~20 CRUD/read tools plus write_page and
    /// create_entry_with_subpage (design 4.4). Pinned - a change here is a
    /// deliberate change to the public substrate's tool surface.
    /// </summary>
    private static readonly string[] ExpectedKernelTools =
    {
        "create_entry", "list_recent", "get_routes", "search",
        "backfill_entry", "backfill_batch", "vault_summary", "read_entry",
        "get_decisions", "get_tasks", "get_blocks", "get_project_context",
        "update_entry", "update_routes", "update_constitution", "get_constitution",
        "update_tags", "start_session", "end_session", "get_sessions",
        "create_entry_with_subpage", "write_page", "organize_entry",
    };

    private static readonly string[] ClosedToolsMustBeAbsent =
    {
        "create_protocol", "get_protocols", "update_protocol", "protocol_respond",
        "execute_behavior", "get_behaviors", "get_agent_scorecard",
        "import_thinker_space", "save_asset", "enrich_entry",
        "list_views", "create_view", "update_view",
    };

    [Fact]
    public void OpenBuild_RegistersOnlyKernelTools_WhenProducerIsNull()
    {
        var savedProducer = Program.Producer;
        try
        {
            Program.Producer = null;

            var registry = Program.BuildToolRegistry(new VaultServices());
            var names = registry.GetToolList().Tools.Select(t => t.Name).ToList();

            names.Should().BeEquivalentTo(ExpectedKernelTools);
            foreach (var closed in ClosedToolsMustBeAbsent)
                names.Should().NotContain(closed);
        }
        finally
        {
            Program.Producer = savedProducer;
        }
    }

    // Bead lmk-9hkn: the open build has no embedding model, so the search
    // tool's semantic parameter always answered "unavailable". Not offered.
    [Fact]
    public void OpenBuild_SearchTool_OffersNoSemanticParameter()
    {
        var savedProducer = Program.Producer;
        try
        {
            Program.Producer = null;

            var search = Program.BuildToolRegistry(new VaultServices()).GetToolList().Tools.Single(t => t.Name == "search");

            search.InputSchema!.Properties!.Keys.Should().NotContain("semantic");
            search.Description.Should().NotContainEquivalentOf("semantic")
                .And.NotContainEquivalentOf("embedding")
                .And.NotContainEquivalentOf("meaning");
        }
        finally
        {
            Program.Producer = savedProducer;
        }
    }

    [Fact]
    public void SearchTool_WithSemanticSearch_KeepsTheParameter()
    {
        var search = new LogMark.Mcp.Tools.SearchTool(new VaultServices(), semanticSearch: true).Definition;

        search.InputSchema!.Properties!.Keys.Should().Contain("semantic");
        search.Description.Should().Contain("semantic=true");
    }

    [Fact]
    public void OpenKernelToolSet_HasExpectedCount()
    {
        // 23 kernel tools. Documented in the Task 4 chunk 4/5/6 report.
        ExpectedKernelTools.Should().HaveCount(23);
    }
}
