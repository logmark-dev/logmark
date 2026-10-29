using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogMark.Services;

public class LlmClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;

    public LlmClient(string baseUrl, string model)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _http = new HttpClient();
    }

    public async Task<LlmResult> ChatAsync(
        string systemPrompt,
        string userPrompt,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        return await ChatAsync(systemPrompt, userPrompt, timeout, temperature: 0.1, maxTokens: 50, ct);
    }

    public async Task<LlmResult> ChatAsync(
        string systemPrompt,
        string userPrompt,
        TimeSpan timeout,
        double temperature,
        int maxTokens,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var request = new ChatRequest
        {
            Model = _model,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userPrompt }
            },
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/v1/chat/completions",
                request,
                cts.Token);

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cts.Token);

            sw.Stop();
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            return new LlmResult(
                Response: content,
                ElapsedMs: sw.ElapsedMilliseconds,
                TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            return new LlmResult(null, sw.ElapsedMilliseconds, TimedOut: true);
        }
        catch (HttpRequestException)
        {
            return new LlmResult(null, sw.ElapsedMilliseconds, TimedOut: false);
        }
        catch (JsonException)
        {
            return new LlmResult(null, sw.ElapsedMilliseconds, TimedOut: false);
        }
    }
}

public record LlmResult(
    string? Response,
    long ElapsedMs,
    bool TimedOut);

// Request/Response models
internal class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    /// <summary>
    /// "none" switches a thinking model's reasoning off. The only request
    /// field LM Studio and Ollama both honour for Qwen 3.5+ on
    /// /v1/chat/completions (probed live 2026-09-02; the chat-template
    /// switches are ignored there). Deadline parsing budgets seconds and a
    /// small output window - any reasoning at all busts it. Servers ignore
    /// unknown fields; LM Studio validates the value and accepts "none".
    /// </summary>
    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; set; } = "none";
}

internal class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

internal class ChatResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

internal class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}
