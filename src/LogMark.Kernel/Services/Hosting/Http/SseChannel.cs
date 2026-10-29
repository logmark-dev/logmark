using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace LogMark.Services.Hosting.Http;

/// <summary>
/// Connected SSE clients for GET /api/v0/events. Frames follow the contract
/// (docs/reference/daemon-http-api-spec.md): `event: vault-event` +
/// `data: {json}`, with `: ping` heartbeat comments at least every 30s.
/// </summary>
public sealed class SseChannel : IDisposable
{
    private readonly ConcurrentDictionary<Guid, HttpListenerResponse> _clients = new();
    private readonly Timer _heartbeat;

    public SseChannel()
    {
        _heartbeat = new Timer(_ => WriteRaw(": ping\n\n"), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public void Attach(HttpListenerResponse response)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;
        var id = Guid.NewGuid();
        _clients[id] = response;
        // Initial comment confirms the stream to eager clients.
        WriteTo(id, response, ": connected\n\n");
    }

    public void Broadcast(string eventName, string jsonData)
    {
        WriteRaw($"event: {eventName}\ndata: {jsonData}\n\n");
    }

    private void WriteRaw(string frame)
    {
        foreach (var (id, response) in _clients)
        {
            WriteTo(id, response, frame);
        }
    }

    private void WriteTo(Guid id, HttpListenerResponse response, string frame)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(frame);
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Flush();
        }
        catch
        {
            // Client went away; drop it.
            _clients.TryRemove(id, out _);
            try { response.Abort(); } catch { }
        }
    }

    public void Dispose()
    {
        _heartbeat.Dispose();
        foreach (var (_, response) in _clients)
        {
            try { response.Abort(); } catch { }
        }
        _clients.Clear();
    }
}
