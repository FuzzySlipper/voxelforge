using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;

namespace VoxelForge.Core.Tests;

public sealed class UndoHistoryServiceTests
{
    [Fact]
    public void ExecuteUndoRedo_UsesExplicitHistoryStateAndPublishesEvents()
    {
        var model = CreateModel();
        var history = new UndoHistoryState(10);
        var events = new ApplicationEventDispatcher();
        var handler = new RecordingUndoHistoryHandler();
        events.Register<UndoHistoryChangedEvent>(handler);
        var service = new UndoHistoryService(NullLogger<UndoHistoryService>.Instance);

        service.Execute(history, events, new SetVoxelCommand(model, new Point3(0, 0, 0), 1));

        Assert.True(service.CanUndo(history));
        Assert.False(service.CanRedo(history));
        Assert.Equal(1, history.UndoCount);
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Single(handler.Events);
        Assert.Equal(UndoHistoryChangeKind.Executed, handler.Events[0].Kind);

        service.Undo(history, events);

        Assert.Null(model.GetVoxel(new Point3(0, 0, 0)));
        Assert.False(service.CanUndo(history));
        Assert.True(service.CanRedo(history));
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
        Assert.Equal(UndoHistoryChangeKind.Undone, handler.Events[1].Kind);

        service.Redo(history, events);

        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.True(service.CanUndo(history));
        Assert.False(service.CanRedo(history));
        Assert.Equal(UndoHistoryChangeKind.Redone, handler.Events[2].Kind);
    }

    [Fact]
    public void SameService_WithTwoHistories_KeepsStateIsolated()
    {
        var firstModel = CreateModel();
        var secondModel = CreateModel();
        var firstHistory = new UndoHistoryState(10);
        var secondHistory = new UndoHistoryState(10);
        var events = new ApplicationEventDispatcher();
        var service = new UndoHistoryService(NullLogger<UndoHistoryService>.Instance);

        service.Execute(firstHistory, events, new SetVoxelCommand(firstModel, new Point3(0, 0, 0), 1));
        service.Execute(secondHistory, events, new SetVoxelCommand(secondModel, new Point3(1, 0, 0), 2));
        service.Undo(firstHistory, events);

        Assert.Null(firstModel.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)2, secondModel.GetVoxel(new Point3(1, 0, 0)));
        Assert.Equal(0, firstHistory.UndoCount);
        Assert.Equal(1, firstHistory.RedoCount);
        Assert.Equal(1, secondHistory.UndoCount);
        Assert.Equal(0, secondHistory.RedoCount);
    }

    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    private sealed class RecordingUndoHistoryHandler : IEventHandler<UndoHistoryChangedEvent>
    {
        public List<UndoHistoryChangedEvent> Events { get; } = [];

        public void Handle(UndoHistoryChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
