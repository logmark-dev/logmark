using System.Text.Json;
using System.Text.Json.Nodes;

namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Writes the group-wrapped JSON hook schema shared by Claude Code
/// (.claude/settings.json), Codex (.codex/hooks.json), and Gemini CLI
/// (.gemini/settings.json):
///   { "hooks": { "<Event>": [ { "hooks": [ { "type":"command", "command":"..." } ] } ] } }
/// Event names and per-hook timeouts come from the HookEntry list, so one
/// writer serves all three hosts. The timeout is per-entry rather than a
/// constant because hosts disagree on the SessionEnd budget in opposite
/// directions - see HookEntry.TimeoutSeconds.
/// </summary>
public sealed class GroupedJsonHookWriter : IHookWriter
{
    public WiringResult Write(string hookPath, HookProfile profile, IReadOnlyList<HookEntry> entries)
    {
        JsonObject root;
        try { root = HookConfigJson.ReadOrCreateRoot(hookPath); }
        catch (JsonException ex)
        {
            return new WiringResult(false, $"Existing hook file is not valid JSON; refusing to modify it: {ex.Message}");
        }

        foreach (var entry in entries)
        {
            var group = new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = entry.Command,
                    ["timeout"] = entry.TimeoutSeconds,
                }),
            };
            HookConfigJson.UpsertItem(root, entry.Event, group);
        }

        return HookConfigJson.WriteValidated(hookPath, root);
    }

    public WiringResult Remove(string hookPath) => HookConfigJson.RemoveMarked(hookPath);

    public bool IsInstalled(string hookPath) => HookConfigJson.FileContainsMarker(hookPath);
}
