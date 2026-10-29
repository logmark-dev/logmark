using System.Text;
using System.Text.Json;
using FluentAssertions;
using LogMark.Mcp;
using LogMark.Mcp.Tools;
using LogMark.Services;
using Xunit;
using LogMark.Services.Pages;

namespace LogMark.Mcp.Tests;

/// <summary>
/// Regression tests for stdin/stdout encoding (Bug 4 from 2026-04-29 vault-write-hardening sprint).
///
/// The 2026-04-29 quant-trading-platform capture had `â` mojibake throughout MCP-written subpages.
/// Diagnostic: McpServer.cs reads bytes one-at-a-time from Console.OpenStandardInput() and casts
/// each byte to char (`var c = (char)buffer[0];`). That treats every byte as Latin-1 / Windows-1252,
/// which silently corrupts every UTF-8 multi-byte sequence (em-dashes, smart quotes, Cyrillic, CJK,
/// emoji, etc.) before it reaches the JSON parser.
///
/// Setting `Console.InputEncoding = Encoding.UTF8` in Program.cs is necessary but NOT sufficient,
/// because OpenStandardInput() returns a raw byte Stream that bypasses the InputEncoding setting.
/// The fix is in McpServer.ReadMessageAsync: read bytes into a buffer, then decode the buffer as
/// UTF-8 once a newline is seen.
/// </summary>
public class McpServerEncodingTests : IDisposable
{
    private readonly string _testVaultPath;

    public McpServerEncodingTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"mcp-enc-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testVaultPath);
        Directory.CreateDirectory(Path.Combine(_testVaultPath, PathConstants.GetSystemFolder()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testVaultPath))
            Directory.Delete(_testVaultPath, recursive: true);
    }

    [Fact]
    public async Task WritePage_ContentWithCyrillic_PreservedOnDisk()
    {
        // Arrange: a write_page request with Cyrillic body text.
        // "Привет, мир!" = "Hello, world!" in Russian. Three-byte UTF-8 codepoints throughout.
        const string cyrillicBody = "# Привет, мир!\n\nЭто проверка кодировки UTF-8.";

        await RunWritePageRoundTripAsync(
            route: "projects/foo",
            type: "decisions",
            filename: "p-cyrillic.md",
            content: cyrillicBody);

        var pagePath = Path.Combine(
            _testVaultPath, "logmark", "projects", "foo", "_decisions", "pages", "p-cyrillic.md");
        File.Exists(pagePath).Should().BeTrue($"page must be written at {pagePath}");

        var written = File.ReadAllText(pagePath, Encoding.UTF8);
        written.Should().Contain("Привет, мир!", "Cyrillic must survive the stdin -> JSON -> disk round trip");
        written.Should().NotContain("Ã", "any Ã/â/etc. mojibake means UTF-8 was decoded as Windows-1252");
        written.Should().NotContain("?", "replacement chars indicate the bytes were lost on read");
    }

    [Fact]
    public async Task WritePage_ContentWithEmDashAndSmartQuotes_PreservedOnDisk()
    {
        // The original capture's `â` mojibake came from em-dashes (U+2014) being read as Latin-1.
        // T8 (HouseStyleNormalizer) maps em-dashes -> "--", but it can only do that if it RECEIVES
        // the em-dash codepoint correctly. If the read boundary corrupts the bytes, T8 sees "â" and
        // passes it through. So we test that the em-dash arrives intact.
        const string body = "Phase 2 — Cross-Sectional Validation Brief";

        await RunWritePageRoundTripAsync(
            route: "projects/foo",
            type: "decisions",
            filename: "p-emdash.md",
            content: body);

        var pagePath = Path.Combine(
            _testVaultPath, "logmark", "projects", "foo", "_decisions", "pages", "p-emdash.md");
        File.Exists(pagePath).Should().BeTrue();

        var written = File.ReadAllText(pagePath, Encoding.UTF8);
        // After T8 the em-dash should be normalized to "--", but T9 only guarantees the BYTES are
        // preserved across the read boundary. Either the original em-dash or its normalized form
        // is acceptable -- what matters is that the corruption sequence "â" never appears.
        written.Should().NotContain("â", "no Latin-1 mojibake of UTF-8 em-dash");
        written.Should().NotContain("Ã", "no double-encoded mojibake either");
        // And the content should still recognizably reference the original phrase
        written.Should().Contain("Phase 2");
        written.Should().Contain("Cross-Sectional Validation Brief");
    }

    [Fact]
    public async Task WritePage_ContentWithEmoji_PreservedOnDisk()
    {
        // Emoji are 4-byte UTF-8 sequences (surrogate pairs in UTF-16). They are the most
        // demanding test of byte-by-byte read correctness because a partial decode produces
        // multiple replacement chars rather than the original character.
        const string body = "Status: ✅ shipped 🚀";

        await RunWritePageRoundTripAsync(
            route: "projects/foo",
            type: "decisions",
            filename: "p-emoji.md",
            content: body);

        var pagePath = Path.Combine(
            _testVaultPath, "logmark", "projects", "foo", "_decisions", "pages", "p-emoji.md");
        File.Exists(pagePath).Should().BeTrue();

        var written = File.ReadAllText(pagePath, Encoding.UTF8);
        written.Should().Contain("✅");
        written.Should().Contain("🚀");
    }

    /// <summary>
    /// Runs a full MCP server round trip: initialize, tools/call write_page with the given
    /// arguments. Streams are MemoryStreams primed with raw UTF-8 bytes -- the same shape a real
    /// MCP client (Claude Desktop, Claude CLI) writes to the server's stdin.
    ///
    /// Critical: this serializer is configured with UnsafeRelaxedJsonEscaping so the request body
    /// contains raw multi-byte UTF-8 sequences instead of \uXXXX escapes. The default System.Text.Json
    /// behavior escapes non-ASCII to ASCII, which would mask the bug because the entire wire payload
    /// would already fit in 7-bit char values.
    /// </summary>
    private async Task RunWritePageRoundTripAsync(string route, string type, string filename, string content)
    {
        var registry = new ToolRegistry();

        var subpageWriter = new SubpageWriter(_testVaultPath);
        var entryPageLinker = new EntryPageLinker(_testVaultPath, subpageWriter);
        var services = new VaultServices
        {
            VaultPath = _testVaultPath,
            SubpageWriter = subpageWriter,
            EntryPageLinker = entryPageLinker
        };
        registry.Register(new WritePageTool(services));

        // Use UnsafeRelaxedJsonEscaping so non-ASCII characters survive as raw UTF-8 bytes on the
        // wire. The default encoder escapes them to \uXXXX, which would hide the read-side encoding
        // bug behind ASCII-only payloads.
        var jsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var initRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2024-11-05" }
        }, jsonOpts);

        var callRequest = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = "write_page",
                arguments = new
                {
                    route,
                    type,
                    filename,
                    content,
                    unlinked = true
                }
            }
        }, jsonOpts);

        var inputData = initRequest + "\n" + callRequest + "\n";
        var inputBytes = Encoding.UTF8.GetBytes(inputData);
        using var inputStream = new MemoryStream(inputBytes);
        using var outputStream = new MemoryStream();

        var server = new McpServer(registry, null, null, inputStream, outputStream);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await server.RunAsync(cts.Token);
    }
}
