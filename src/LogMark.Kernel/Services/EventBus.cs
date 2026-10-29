using System.Collections.Concurrent;

namespace LogMark.Services;

public class EventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public IDisposable Subscribe<T>(Action<T> handler)
    {
        var eventType = typeof(T);
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Delegate> { handler },
            (_, list) =>
            {
                var newList = new List<Delegate>(list) { handler };
                return newList;
            });

        return new Subscription(() => Unsubscribe(eventType, handler));
    }

    private void Unsubscribe(Type eventType, Delegate handler)
    {
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Delegate>(),
            (_, list) =>
            {
                var newList = new List<Delegate>(list);
                newList.Remove(handler);
                return newList;
            });
    }

    public void Publish<T>(T evt)
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
        {
            foreach (var handler in list.ToArray())
            {
                try
                {
                    ((Action<T>)handler)(evt);
                }
                catch (Exception)
                {
                    // Swallow - one bad handler shouldn't break others
                }
            }
        }
    }

    private class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
