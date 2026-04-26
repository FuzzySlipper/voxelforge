namespace VoxelForge.App.Events;

/// <summary>
/// Marker for application/domain events published across VoxelForge components.
/// </summary>
public interface IApplicationEvent
{
}

/// <summary>
/// Publishes typed application events without exposing dispatcher registration.
/// </summary>
public interface IEventPublisher
{
    void Publish<TEvent>(TEvent applicationEvent) where TEvent : IApplicationEvent;
}

/// <summary>
/// Handles one concrete application event type.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IApplicationEvent
{
    void Handle(TEvent applicationEvent);
}

/// <summary>
/// Registers named handlers and publishes events to explicitly registered subscribers.
/// </summary>
public interface IEventDispatcher : IEventPublisher
{
    void Register<TEvent>(IEventHandler<TEvent> handler) where TEvent : IApplicationEvent;
}

/// <summary>
/// In-process typed event dispatcher. Registration is explicit and exact-type based;
/// no reflection or automatic scanning is used.
/// </summary>
public sealed class ApplicationEventDispatcher : IEventDispatcher
{
    private readonly Dictionary<Type, List<object>> _handlers = [];

    public void Register<TEvent>(IEventHandler<TEvent> handler) where TEvent : IApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers = [];
            _handlers[eventType] = handlers;
        }

        handlers.Add(handler);
    }

    public void Publish<TEvent>(TEvent applicationEvent) where TEvent : IApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var handlers))
            return;

        var snapshot = handlers.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
            ((IEventHandler<TEvent>)snapshot[i]).Handle(applicationEvent);
    }
}
