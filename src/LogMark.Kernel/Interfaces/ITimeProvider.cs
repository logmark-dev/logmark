namespace LogMark.Services;

public interface ITimeProvider
{
    DateTime Now { get; }
}
