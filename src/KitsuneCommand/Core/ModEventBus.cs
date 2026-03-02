using System.Collections.Concurrent;

namespace KitsuneCommand.Core
{
    /// <summary>
    /// Thread-safe publish/subscribe event bus implementation.
    /// </summary>
    public class ModEventBus : IModEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new ConcurrentDictionary<Type, List<Delegate>>();
        private readonly object _lock = new object();

        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(TEvent);
            lock (_lock)
            {
                if (!_handlers.TryGetValue(eventType, out var list))
                {
                    list = new List<Delegate>();
                    _handlers[eventType] = list;
                }
                list.Add(handler);
            }
        }

        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventType = typeof(TEvent);
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var list))
                {
                    list.Remove(handler);
                }
            }
        }

        public void Publish<TEvent>(TEvent eventData)
        {
            var eventType = typeof(TEvent);
            List<Delegate> snapshot;

            lock (_lock)
            {
                if (!_handlers.TryGetValue(eventType, out var list) || list.Count == 0)
                    return;

                snapshot = new List<Delegate>(list);
            }

            foreach (var handler in snapshot)
            {
                try
                {
                    ((Action<TEvent>)handler)(eventData);
                }
                catch (Exception ex)
                {
                    Log.Error($"[KitsuneCommand] Error in event handler for {eventType.Name}: {ex.Message}");
                    Log.Exception(ex);
                }
            }
        }
    }
}
