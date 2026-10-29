namespace LogMark.Services.Agents.ConfigWriters;

// Handles the "servers" config key used by VS Code and similar agents.
// The base class already uses configKey dynamically, so no overrides are needed.
public class JsonServersWriter : JsonMcpServersWriter
{
}
