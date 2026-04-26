using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.Tests;

public sealed class VoxelEditingServiceTests
{
    [Fact]
    public void ApplyMutationIntent_MutatesThroughUndoAndPublishesEvents()
    {
        var model = CreateModel();
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var modelEvents = new RecordingModelChangedHandler();
        var undoEvents = new RecordingUndoHistoryHandler();
        events.Register<VoxelModelChangedEvent>(modelEvents);
        events.Register<UndoHistoryChangedEvent>(undoEvents);
        var undoStack = CreateUndoStack(events);

        var intentResult = new VoxelMutationIntentService().BuildSetIntent([
            new VoxelAssignmentRequest(new Point3(0, 0, 0), 1),
            new VoxelAssignmentRequest(new Point3(1, 0, 0), 1),
        ]);
        Assert.NotNull(intentResult.Intent);

        var service = new VoxelEditingService();
        var result = service.ApplyMutationIntent(
            document,
            undoStack,
            events,
            new ApplyVoxelMutationIntentRequest(intentResult.Intent));

        Assert.True(result.Success);
        Assert.Equal(2, model.GetVoxelCount());
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Single(modelEvents.Events);
        Assert.Equal(VoxelModelChangeKind.SetVoxel, modelEvents.Events[0].Kind);
        Assert.Single(undoEvents.Events);
        Assert.Equal(UndoHistoryChangeKind.Executed, undoEvents.Events[0].Kind);

        undoStack.Undo();
        Assert.Equal(0, model.GetVoxelCount());
        Assert.Equal(2, undoEvents.Events.Count);
        Assert.Equal(UndoHistoryChangeKind.Undone, undoEvents.Events[1].Kind);

        undoStack.Redo();
        Assert.Equal(2, model.GetVoxelCount());
        Assert.Equal(3, undoEvents.Events.Count);
        Assert.Equal(UndoHistoryChangeKind.Redone, undoEvents.Events[2].Kind);
    }

    [Fact]
    public void Clear_RemovesVoxelsThroughSingleUndoableOperation()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var modelEvents = new RecordingModelChangedHandler();
        events.Register<VoxelModelChangedEvent>(modelEvents);
        var undoStack = CreateUndoStack(events);

        var result = new VoxelEditingService().Clear(document, undoStack, events);

        Assert.True(result.Success);
        Assert.Equal("Cleared 2 voxels.", result.Message);
        Assert.Equal(0, model.GetVoxelCount());
        Assert.Single(modelEvents.Events);
        Assert.Equal(VoxelModelChangeKind.Clear, modelEvents.Events[0].Kind);

        undoStack.Undo();
        Assert.Equal(2, model.GetVoxelCount());
    }

    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    private static LabelIndex CreateLabels() => new(NullLogger<LabelIndex>.Instance);

    private static UndoStack CreateUndoStack(IEventPublisher events) =>
        new(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);

    private sealed class RecordingModelChangedHandler : IEventHandler<VoxelModelChangedEvent>
    {
        public List<VoxelModelChangedEvent> Events { get; } = [];

        public void Handle(VoxelModelChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }

    private sealed class RecordingUndoHistoryHandler : IEventHandler<UndoHistoryChangedEvent>
    {
        public List<UndoHistoryChangedEvent> Events { get; } = [];

        public void Handle(UndoHistoryChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
