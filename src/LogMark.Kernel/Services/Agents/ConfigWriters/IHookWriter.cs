namespace LogMark.Services.Agents.ConfigWriters;

/// <summary>
/// Writes / removes LogMark lifecycle hook command registrations in one host's
/// hook config file. Implementations differ only in the leaf schema they emit;
/// merge, validation, removal, and marker detection are shared via HookConfigJson.
/// </summary>
public interface IHookWriter
{
    WiringResult Write(string hookPath, HookProfile profile, IReadOnlyList<HookEntry> entries);
    WiringResult Remove(string hookPath);
    bool IsInstalled(string hookPath);
}
