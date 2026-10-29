using FluentAssertions;
using LogMark.Mcp.Prompts;
using Xunit;

namespace LogMark.Mcp.Tests.Prompts;

public class StudyThinkerPromptPolymathTests
{
    private readonly StudyThinkerPrompt _prompt = new();

    [Theory]
    [InlineData("graham", "Paul Graham", "paulgraham.com")]
    [InlineData("paulgraham", "Paul Graham", "paulgraham.com")]
    [InlineData("cowen", "Tyler Cowen", "marginalrevolution.com")]
    [InlineData("tylercowen", "Tyler Cowen", "marginalrevolution.com")]
    [InlineData("linuslee", "Linus Lee", "thesephist.com")]
    [InlineData("thesephist", "Linus Lee", "thesephist.com")]
    [InlineData("dhh", "David Heinemeier Hansson", "dhh")]
    [InlineData("ciechanowski", "Bartosz Ciechanowski", "ciechanow.ski")]
    [InlineData("victor", "Bret Victor", "worrydream.com")]
    [InlineData("karlsson", "Henrik Karlsson", "henrikkarlsson.xyz")]
    [InlineData("webb", "Matt Webb", "interconnected.org")]
    public async Task GetAsync_WithPolymathThinker_IncludesCorrectContext(
        string key, string expectedName, string expectedUrl)
    {
        var args = new Dictionary<string, string> { ["thinker"] = key };

        var result = await _prompt.GetAsync(args, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain(expectedName);
        text.Should().Contain(expectedUrl);
    }

    [Fact]
    public async Task GetAsync_GrahamRoute_DefaultsToResearchGraham()
    {
        var args = new Dictionary<string, string> { ["thinker"] = "graham" };

        var result = await _prompt.GetAsync(args, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("research/graham");
    }

    [Fact]
    public async Task GetAsync_CowenRoute_DefaultsToResearchCowen()
    {
        var args = new Dictionary<string, string> { ["thinker"] = "cowen" };

        var result = await _prompt.GetAsync(args, CancellationToken.None);

        var text = result.Messages[0].Content.Text!;
        text.Should().Contain("research/cowen");
    }
}
