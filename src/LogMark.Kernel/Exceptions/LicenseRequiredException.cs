namespace LogMark.Exceptions;

public class LicenseRequiredException : Exception
{
    public string Feature { get; }

    public LicenseRequiredException(string feature)
        : base($"Feature '{feature}' requires a Pro license.")
    {
        Feature = feature;
    }
}
