using FluentAssertions;
using LogMark.Mcp.Prompts;
using LogMark.Mcp.Protocol;
using LogMark.Mcp.Tools;
using Xunit;

namespace LogMark.Mcp.Tests;

public class VaultToolsGuidePromptTests
{
    private readonly ToolRegistry _toolRegistry;
    private readonly VaultToolsGuidePrompt _prompt;

    public VaultToolsGuidePromptTests()
    {
        _toolRegistry = new ToolRegistry();
        _toolRegistry.Register(new DummyTool("create_entry", "Create a new entry in the vault"));
        _toolRegistry.Register(new DummyTool("search", "Search entries by text or tags"));

        _prompt = new VaultToolsGuidePrompt(_toolRegistry);
    }

    [Fact]
    public async Task GetAsync_IncludesAllRegisteredToolNames()
    {
        var result = await _prompt.GetAsync(null, CancellationToken.None);

        result.Messages.Should().HaveCount(1);
        result.Messages[0].Role.Should().Be("user");
        result.Messages[0].Content.Type.Should().Be("text");

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("create_entry");
        text.Should().Contain("search");
        text.Should().Contain("Create a new entry in the vault");
        text.Should().Contain("Search entries by text or tags");
    }

    [Fact]
    public async Task GetAsync_IncludesCompositionPatterns()
    {
        var result = await _prompt.GetAsync(null, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;

        // Verify the four composition patterns from the spec
        text.Should().Contain("create_entry");
        text.Should().Contain("write_page");
        text.Should().Contain("organize_entry");
        text.Should().NotContain("set_entry_rating");
        text.Should().NotContain("flag_entry");
        text.Should().Contain("start_session");
        text.Should().Contain("end_session");
    }

    // Bead lmk-068p: the open build has no save_asset tool, yet the guide
    // told agents to call it. The image workflow appears only with the tool.
    [Fact]
    public async Task GetAsync_OmitsTheImageWorkflow_WhenSaveAssetIsNotRegistered()
    {
        var text = (await _prompt.GetAsync(null, CancellationToken.None)).Messages[0].Content.Text!;

        text.Should().NotContain("save_asset");
        text.Should().NotContain("Entry with images");
    }

    [Fact]
    public async Task GetAsync_IncludesTheImageWorkflow_WhenSaveAssetIsRegistered()
    {
        _toolRegistry.Register(new DummyTool("save_asset", "Save an image to the vault"));

        var text = (await _prompt.GetAsync(null, CancellationToken.None)).Messages[0].Content.Text!;

        text.Should().Contain("Entry with images");
        text.Should().Contain("`save_asset`");
    }

    [Fact]
    public async Task GetAsync_DoesNotNameTheDesktopViewer()
    {
        var text = (await _prompt.GetAsync(null, CancellationToken.None)).Messages[0].Content.Text!;

        text.Should().NotContain("desktop viewer");
    }

    [Fact]
    public async Task GetAsync_IncludesInputSchemaAsJson()
    {
        var result = await _prompt.GetAsync(null, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;

        // The dummy tool has a "query" property -- its schema should be serialized
        text.Should().Contain("\"query\"");
        text.Should().Contain("\"type\"");
    }

    [Fact]
    public void Definition_HasCorrectNameAndDescription()
    {
        _prompt.Name.Should().Be("vault-tools-guide");
        _prompt.Definition.Name.Should().Be("vault-tools-guide");
        _prompt.Definition.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetAsync_WithNoTools_StillIncludesCompositionPatterns()
    {
        var emptyRegistry = new ToolRegistry();
        var prompt = new VaultToolsGuidePrompt(emptyRegistry);

        var result = await prompt.GetAsync(null, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("start_session");
        text.Should().Contain("organize_entry");
    }

    /// <summary>
    /// Minimal ITool implementation for testing the prompt's tool enumeration.
    /// </summary>
    private class DummyTool : ITool
    {
        public DummyTool(string name, string description)
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
                        ["query"] = new()
                        {
                            Type = "string",
                            Description = "Search query text"
                        }
                    },
                    Required = new List<string> { "query" }
                }
            };
        }

        public string Name { get; }
        public ToolDefinition Definition { get; }

        public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object?>? arguments, CancellationToken ct)
        {
            throw new NotImplementedException("Dummy tool - not for execution");
        }
    }
}
