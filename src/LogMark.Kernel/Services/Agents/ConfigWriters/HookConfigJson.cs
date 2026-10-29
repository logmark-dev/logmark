using System.Text.Json;
using System.Text.Json.Nodes;
using LogMark.Services.Agents;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Shared read / merge / validate / remove machinery for the JSON hook config
/// writers. LogMark-owned hook items are identified by the agent-context /
/// agent-capture command markers (the same substring-marker idiom the MCP
/// JsonMcpServersWriter uses for "logmark-mcp"). All hook schemas share the
/// shape { "hooks": { "<event>": [ <item>, ... ] } }, so removal and marker
/// detection are writer-agnostic; only insertion (the leaf item) differs.
/// This is the only hook file that performs direct file I/O.
/// </summary>
internal static class HookConfigJson
{
    internal static readonly string[] Markers = ["agent-context", "agent-capture"];

    internal static JsonObject ReadOrCreateRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        return JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("hook config root is not a JSON object");
    }

    internal static bool ContainsMarker(JsonNode node) =>
        Markers.Any(m => node.ToJsonString().Contains(m, StringComparison.OrdinalIgnoreCase));

    internal static bool FileContainsMarker(string path)
    {
        if (!File.Exists(path)) return false;
        try { return Markers.Any(m => File.ReadAllText(path).Contains(m, StringComparison.OrdinalIgnoreCase)); }
        catch { return false; }
    }

    /// <summary>Drops every LogMark-marked item from every event array, pruning
    /// emptied event keys and an emptied top-level hooks object.</summary>
    internal static WiringResult RemoveMarked(string path)
    {
        if (!File.Exists(path)) return new WiringResult(true);

        JsonObject root;
        try { root = ReadOrCreateRoot(path); }
        catch (JsonException ex)
        {
            return new WiringResult(false, $"Existing hook file is not valid JSON; refusing to modify it: {ex.Message}");
        }

        if (root["hooks"] is JsonObject hooks)
        {
            foreach (var eventName in hooks.Select(kv => kv.Key).ToList())
            {
                if (hooks[eventName] is not JsonArray items) continue;
                var kept = new JsonArray();
                foreach (var item in items.ToList())
                {
                    items.Remove(item);
                    if (item is not null && !ContainsMarker(item)) kept.Add(item);
                }
                if (kept.Count > 0) hooks[eventName] = kept;
                else hooks.Remove(eventName);
            }
            if (hooks.Count == 0) root.Remove("hooks");
        }

        return WriteValidated(path, root);
    }

    internal static WiringResult WriteValidated(string path, JsonObject root)
    {
        try
        {
            var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            using (JsonDocument.Parse(output)) { } // validate before touching disk

            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, output);
            return new WiringResult(true);
        }
        catch (Exception ex)
        {
            return new WiringResult(false, $"Failed to write hooks: {ex.Message}");
        }
    }

    /// <summary>Inserts an item into hooks.<event>, replacing any existing
    /// LogMark-marked item under that event first (idempotent reinstall).</summary>
    internal static void UpsertItem(JsonObject root, string eventName, JsonObject item)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        var items = hooks[eventName] as JsonArray ?? new JsonArray();
        foreach (var existing in items.ToList())
            if (existing is not null && ContainsMarker(existing)) items.Remove(existing);
        items.Add(item);
        hooks[eventName] = items;
    }
}
