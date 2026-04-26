using VoxelForge.App.Events;

namespace VoxelForge.Core.Tests;

public sealed class EventDispatcherTests
{
    [Fact]
    public void Publish_DispatchesToRegisteredHandler()
    {
        var dispatcher = new ApplicationEventDispatcher();
        var handler = new RecordingModelChangedHandler();
        dispatcher.Register<VoxelModelChangedEvent>(handler);

        var applicationEvent = new VoxelModelChangedEvent(
            VoxelModelChangeKind.SetVoxel,
            "set one voxel",
            1);
        dispatcher.Publish(applicationEvent);

        Assert.Single(handler.Events);
        Assert.Same(applicationEvent, handler.Events[0]);
    }

    [Fact]
    public void Publish_DoesNotDispatchUnregisteredEventTypes()
    {
        var dispatcher = new ApplicationEventDispatcher();
        var handler = new RecordingModelChangedHandler();
        dispatcher.Register<VoxelModelChangedEvent>(handler);

        dispatcher.Publish(new PaletteChangedEvent(
            PaletteChangeKind.EntryAdded,
            "palette added",
            1,
            1));

        Assert.Empty(handler.Events);
    }

    private sealed class RecordingModelChangedHandler : IEventHandler<VoxelModelChangedEvent>
    {
        public List<VoxelModelChangedEvent> Events { get; } = [];

        public void Handle(VoxelModelChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
