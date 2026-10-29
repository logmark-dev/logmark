using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogMark.Mcp.Protocol;

/// <summary>
/// JSON-RPC 2.0 request message.
/// </summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    public bool IsNotification => Id == null || Id.Value.ValueKind == JsonValueKind.Null;
}

/// <summary>
/// JSON-RPC 2.0 response message.
/// </summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? ErrorData { get; set; }

    public static JsonRpcResponse Success(JsonElement? id, object result) => new()
    {
        Id = id,
        Result = result
    };

    public static JsonRpcResponse Error(JsonElement? id, int code, string message, object? data = null) => new()
    {
        Id = id,
        ErrorData = new JsonRpcError { Code = code, Message = message, Data = data }
    };

    public static JsonRpcResponse ParseError(JsonElement? id = null) =>
        Error(id, -32700, "Parse error");

    public static JsonRpcResponse InvalidRequest(JsonElement? id = null) =>
        Error(id, -32600, "Invalid Request");

    public static JsonRpcResponse MethodNotFound(JsonElement? id, string method) =>
        Error(id, -32601, $"Method not found: {method}");

    public static JsonRpcResponse InvalidParams(JsonElement? id, string message) =>
        Error(id, -32602, $"Invalid params: {message}");

    public static JsonRpcResponse InternalError(JsonElement? id, string message) =>
        Error(id, -32603, $"Internal error: {message}");
}

/// <summary>
/// JSON-RPC 2.0 error object.
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }
}
