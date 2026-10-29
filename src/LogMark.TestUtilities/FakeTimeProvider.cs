using System;
using LogMark.Services;

namespace LogMark.TestUtilities;

public class FakeTimeProvider : ITimeProvider
{
    private DateTime _current;

    public FakeTimeProvider(DateTime start) => _current = start;

    public DateTime Now => _current;

    public void Advance(TimeSpan duration) => _current = _current.Add(duration);

    public void Set(DateTime time) => _current = time;
}
