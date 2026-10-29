namespace LogMark.Services;

/// <summary>
/// Tracks session boundaries based on inactivity threshold
/// Thread-safe singleton pattern
/// </summary>
public class SessionTracker
{
    private readonly object _lock = new();
    private readonly double _inactivityThresholdMinutes;
    private string _currentSessionId;
    private DateTime _lastEventTime;

    public SessionTracker(double inactivityThresholdMinutes = 30.0)
    {
        _inactivityThresholdMinutes = inactivityThresholdMinutes;
        _currentSessionId = Guid.NewGuid().ToString();
        _lastEventTime = DateTime.Now;
    }

    /// <summary>
    /// Get current session ID, generating new one if inactivity threshold exceeded
    /// </summary>
    public string GetCurrentSessionId()
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            var timeSinceLastEvent = now - _lastEventTime;

            // Generate new session if threshold exceeded
            if (timeSinceLastEvent.TotalMinutes > _inactivityThresholdMinutes)
            {
                _currentSessionId = Guid.NewGuid().ToString();
            }

            _lastEventTime = now;
            return _currentSessionId;
        }
    }
}
