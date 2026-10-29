using FluentAssertions;
using LogMark.Mcp.Prompts;
using Xunit;

namespace LogMark.Mcp.Tests.Prompts;

public class StudyThinkerPromptTests
{
    private readonly StudyThinkerPrompt _prompt = new();

    [Fact]
    public void Definition_HasCorrectNameAndArguments()
    {
        _prompt.Name.Should().Be("study-thinker");
        _prompt.Definition.Name.Should().Be("study-thinker");

        var args = _prompt.Definition.Arguments;
        args.Should().NotBeNull();
        args.Should().HaveCount(3);

        var thinker = args!.First(a => a.Name == "thinker");
        thinker.Required.Should().BeTrue();

        var scope = args!.First(a => a.Name == "scope");
        scope.Required.Should().BeFalse();

        var route = args!.First(a => a.Name == "route");
        route.Required.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithKnownThinker_IncludesThinkerContext()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "sivers" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("Derek Sivers");
        text.Should().Contain("sive.rs");
        text.Should().Contain("Known thinker");
    }

    [Fact]
    public async Task GetAsync_WithKnownThinker_CaseInsensitive()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "Karpathy" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("Andrej Karpathy");
    }

    [Fact]
    public async Task GetAsync_WithUrl_TreatsAsDirectSource()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "https://example.com/blog" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("https://example.com/blog");
        text.Should().NotContain("Known thinker");
    }

    [Fact]
    public async Task GetAsync_WithUnknownName_GivesGenericGuidance()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "someone-unknown" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("someone-unknown");
        text.Should().NotContain("Known thinker");
    }

    [Fact]
    public async Task GetAsync_WithRouteArgument_IncludesRouteInPrompt()
    {
        var arguments = new Dictionary<string, string>
        {
            ["thinker"] = "sivers",
            ["route"] = "research/business"
        };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("research/business");
    }

    [Fact]
    public async Task GetAsync_WithScopeArgument_IncludesScopeInPrompt()
    {
        var arguments = new Dictionary<string, string>
        {
            ["thinker"] = "sivers",
            ["scope"] = "deep"
        };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("deep");
    }

    [Fact]
    public async Task GetAsync_IncludesTwoPassMethodology()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "sivers" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("PASS 1");
        text.Should().Contain("PASS 2");
        text.Should().Contain("Leonardo");
        text.Should().Contain("create_entry");
        text.Should().Contain("write_page");
    }

    [Fact]
    public async Task GetAsync_IncludesProvenanceMetadataRequirements()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "sivers" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("origin: thinktank");
        text.Should().Contain("via:");
        text.Should().Contain("via_url:");
        text.Should().Contain("crawled:");
    }

    [Fact]
    public async Task GetAsync_DefaultScope_IsRecent()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "sivers" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("recent");
    }

    [Fact]
    public async Task GetAsync_DefaultRoute_UsesResearchPrefix()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "sivers" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("research/sivers");
    }

    [Fact]
    public async Task GetAsync_IncludesCacheCheckGuidance()
    {
        var arguments = new Dictionary<string, string> { ["thinker"] = "karpathy" };

        var result = await _prompt.GetAsync(arguments, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("import_thinker_space");
    }
}
