using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp.Protocol;
using Xunit;

namespace LogMark.Mcp.Tests;

public class ToolMetadataSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [Fact]
    public void Annotations_SerializeUnderAnnotationsKey_WithExactFieldNames()
    {
        var def = new ToolDefinition
        {
            Name = "x",
            Description = "d",
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = true,
                DestructiveHint = false,
                IdempotentHint = true,
                OpenWorldHint = false,
            },
        };

        var json = JsonSerializer.Serialize(def, Options);

        json.Should().Contain("\"annotations\":{");
        json.Should().Contain("\"readOnlyHint\":true");
        json.Should().Contain("\"destructiveHint\":false");
        json.Should().Contain("\"idempotentHint\":true");
        json.Should().Contain("\"openWorldHint\":false");
    }

    [Fact]
    public void Annotations_And_Meta_OmittedWhenNull()
    {
        var def = new ToolDefinition { Name = "x", Description = "d" };

        var json = JsonSerializer.Serialize(def, Options);

        json.Should().NotContain("annotations");
        json.Should().NotContain("_meta");
    }

    [Fact]
    public void Meta_SerializesUnderUnderscoreMetaKey()
    {
        var def = new ToolDefinition
        {
            Name = "x",
            Description = "d",
            Meta = new Dictionary<string, object?> { ["anthropic/alwaysLoad"] = true },
        };

        var json = JsonSerializer.Serialize(def, Options);

        json.Should().Contain("\"_meta\":{\"anthropic/alwaysLoad\":true}");
    }

    [Fact]
    public void UnsetAnnotationFields_AreOmitted()
    {
        var def = new ToolDefinition
        {
            Name = "x",
            Description = "d",
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        };

        var json = JsonSerializer.Serialize(def, Options);

        json.Should().Contain("\"readOnlyHint\":true");
        json.Should().NotContain("destructiveHint");
        json.Should().NotContain("idempotentHint");
        json.Should().NotContain("openWorldHint");
    }

    [Fact]
    public void InitializeResult_Instructions_SerializeWhenSet_OmitWhenNull()
    {
        var with = new InitializeResult { Instructions = "hello agent" };
        var without = new InitializeResult();

        JsonSerializer.Serialize(with, Options).Should().Contain("\"instructions\":\"hello agent\"");
        JsonSerializer.Serialize(without, Options).Should().NotContain("instructions");
    }
}
