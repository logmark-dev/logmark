namespace LogMark.Exceptions;

/// <summary>
/// Thrown when attempting to initialize a vault that already has a LogMark structure.
/// Use the force flag to overwrite an existing vault.
/// </summary>
public class VaultExistsException : Exception
{
    public string VaultPath { get; }

    public VaultExistsException(string path)
        : base($"LogMark structure already exists at: {path}")
    {
        VaultPath = path;
    }

    public VaultExistsException(string path, string message)
        : base(message)
    {
        VaultPath = path;
    }
}
