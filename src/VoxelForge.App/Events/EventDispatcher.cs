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
    void Publish(IApplicationEvent applicationEvent);
    void PublishAll(IReadOnlyList<IApplicationEvent> applicationEvents);
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
/// Register handlers during application composition before background publishers start.
/// </summary>
public interface IEventDispatcher : IEventPublisher
{
    void Register<TEvent>(IEventHandler<TEvent> handler) where TEvent : IApplicationEvent;
}

/// <summary>
/// In-process typed event dispatcher. Registration is explicit and exact-type based;
/// no reflection or automatic scanning is used. Registration is a composition-time
/// operation; publishing is synchronous on the caller's thread and does not marshal
/// to the UI thread. If future code needs dynamic late registration, add a deliberate
/// synchronization strategy instead of relying on the current plain dictionary.
/// </summary>
public sealed class ApplicationEventDispatcher : IEventDispatcher
{
    private readonly Dictionary<Type, List<IApplicationEventHandlerInvoker>> _handlers = [];

    public void Register<TEvent>(IEventHandler<TEvent> handler) where TEvent : IApplicationEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers = [];
            _handlers[eventType] = handlers;
        }

        handlers.Add(new ApplicationEventHandlerInvoker<TEvent>(handler));
    }

    public void Publish<TEvent>(TEvent applicationEvent) where TEvent : IApplicationEvent
    {
        Publish((IApplicationEvent)applicationEvent);
    }

    public void Publish(IApplicationEvent applicationEvent)
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);

        var eventType = applicationEvent.GetType();
        if (!_handlers.TryGetValue(eventType, out var handlers))
            return;

        var snapshot = handlers.ToArray();
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i].Handle(applicationEvent);
    }

    public void PublishAll(IReadOnlyList<IApplicationEvent> applicationEvents)
    {
        ArgumentNullException.ThrowIfNull(applicationEvents);

        for (int i = 0; i < applicationEvents.Count; i++)
            Publish(applicationEvents[i]);
    }

    private interface IApplicationEventHandlerInvoker
    {
        void Handle(IApplicationEvent applicationEvent);
    }

    private sealed class ApplicationEventHandlerInvoker<TEvent> : IApplicationEventHandlerInvoker
        where TEvent : IApplicationEvent
    {
        private readonly IEventHandler<TEvent> _handler;

        public ApplicationEventHandlerInvoker(IEventHandler<TEvent> handler)
        {
            _handler = handler;
        }

        public void Handle(IApplicationEvent applicationEvent)
        {
            _handler.Handle((TEvent)applicationEvent);
        }
    }
}
