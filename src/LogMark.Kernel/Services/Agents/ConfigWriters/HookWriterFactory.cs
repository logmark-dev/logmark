namespace LogMark.Services.Agents.ConfigWriters;

public static class HookWriterFactory
{
    public static IHookWriter Create(HookWriterKind kind) => kind switch
    {
        HookWriterKind.GroupedJson => new GroupedJsonHookWriter(),
        HookWriterKind.CursorJson => new CursorJsonHookWriter(),
        HookWriterKind.CopilotJson => new CopilotJsonHookWriter(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown hook writer kind"),
    };
}
