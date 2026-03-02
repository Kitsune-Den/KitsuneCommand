using System;

namespace KitsuneCommand.Abstractions
{
    /// <summary>
    /// Publish/subscribe event bus for game events and mod communication.
    /// </summary>
    public interface IModEventBus
    {
        /// <summary>
        /// Subscribe to events of type TEvent.
        /// </summary>
        void Subscribe<TEvent>(Action<TEvent> handler);

        /// <summary>
        /// Unsubscribe from events of type TEvent.
        /// </summary>
        void Unsubscribe<TEvent>(Action<TEvent> handler);

        /// <summary>
        /// Publish an event to all subscribers.
        /// </summary>
        void Publish<TEvent>(TEvent eventData);
    }
}
