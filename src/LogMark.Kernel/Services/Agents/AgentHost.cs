namespace LogMark.Services.Agents;

/// <summary>
/// Which OS surface an agent's binaries and config files live on. Windows is
/// the default and describes every agent that shares the desktop app's own
/// filesystem and PATH. Wsl describes an agent installed inside a WSL distro,
/// reached across the boundary: its config paths resolve to
/// \\wsl.localhost\... UNC form and its commands are probed and launched
/// through wsl.exe. See WslBridge, and
/// docs/plans/2026-07-31-grok-wsl-agent-integration-design.md.
/// </summary>
public enum AgentHost
{
    Windows,
    Wsl
}
