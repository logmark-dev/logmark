using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Writes Cursor's flat hook schema (.cursor/hooks.json):
///   { "version": 1, "hooks": { "sessionStart": [ { "command": "..." } ] } }
/// Items are bare { "command" } objects and the file requires a top-level
/// "version": 1.
/// </summary>
public sealed class CursorJsonHookWriter : IHookWriter
{
    public WiringResult Write(string hookPath, HookProfile profile, IReadOnlyList<HookEntry> entries)
    {
        JsonObject root;
        try { root = HookConfigJson.ReadOrCreateRoot(hookPath); }
        catch (JsonException ex)
        {
            return new WiringResult(false, $"Existing hook file is not valid JSON; refusing to modify it: {ex.Message}");
        }

        root["version"] = 1;
        foreach (var entry in entries)
            HookConfigJson.UpsertItem(root, entry.Event, new JsonObject { ["command"] = entry.Command });

        return HookConfigJson.WriteValidated(hookPath, root);
    }

    public WiringResult Remove(string hookPath) => HookConfigJson.RemoveMarked(hookPath);

    public bool IsInstalled(string hookPath) => HookConfigJson.FileContainsMarker(hookPath);
}
