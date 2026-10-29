namespace LogMark.Services.Agents;

public interface IRegistryProbe
{
    IEnumerable<string> EnumerateSubKeys(string keyPath);
}
