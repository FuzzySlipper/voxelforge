using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

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
    public void ApplyMutationIntent_ReportsMixedKindForSetAndRemoveAssignments()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(1, 0, 0), 1);
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var modelEvents = new RecordingModelChangedHandler();
        events.Register<VoxelModelChangedEvent>(modelEvents);
        var undoStack = CreateUndoStack(events);
        var intent = new VoxelMutationIntent
        {
            Assignments =
            [
                new VoxelAssignment(new Point3(0, 0, 0), 1),
                new VoxelAssignment(new Point3(1, 0, 0), null),
            ],
            Description = "Mixed edit",
        };

        var result = new VoxelEditingService().ApplyMutationIntent(
            document,
            undoStack,
            events,
            new ApplyVoxelMutationIntentRequest(intent));

        Assert.True(result.Success);
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Null(model.GetVoxel(new Point3(1, 0, 0)));
        Assert.Single(modelEvents.Events);
        Assert.Equal(VoxelModelChangeKind.MixedVoxelEdit, modelEvents.Events[0].Kind);

        undoStack.Undo();
        Assert.Null(model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(1, 0, 0)));
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

    [Fact]
    public void RemoveVoxel_ClearsLabelAndUndoRestoresIt()
    {
        var model = CreateModel();
        var labels = CreateLabels();
        var position = new Point3(0, 0, 0);
        var regionId = new RegionId("body");
        model.SetVoxel(position, 1);
        labels.AddOrUpdateRegion(new RegionDef { Id = regionId, Name = "body" });
        labels.AssignRegion(regionId, [position]);
        var document = new EditorDocumentState(model, labels);
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new VoxelEditingService().RemoveVoxel(
            document,
            undoStack,
            events,
            new RemoveVoxelRequest(position));

        Assert.True(result.Success);
        Assert.Null(model.GetVoxel(position));
        Assert.Null(labels.GetRegion(position));

        undoStack.Undo();

        Assert.Equal((byte)1, model.GetVoxel(position));
        Assert.Equal(regionId, labels.GetRegion(position));
    }

    [Fact]
    public void Clear_ClearsLabelsAndUndoRestoresThem()
    {
        var model = CreateModel();
        var labels = CreateLabels();
        var first = new Point3(0, 0, 0);
        var second = new Point3(1, 0, 0);
        var regionId = new RegionId("body");
        model.SetVoxel(first, 1);
        model.SetVoxel(second, 1);
        labels.AddOrUpdateRegion(new RegionDef { Id = regionId, Name = "body" });
        labels.AssignRegion(regionId, [first, second]);
        var document = new EditorDocumentState(model, labels);
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new VoxelEditingService().Clear(document, undoStack, events);

        Assert.True(result.Success);
        Assert.Equal(0, model.GetVoxelCount());
        Assert.Null(labels.GetRegion(first));
        Assert.Null(labels.GetRegion(second));

        undoStack.Undo();

        Assert.Equal(2, model.GetVoxelCount());
        Assert.Equal(regionId, labels.GetRegion(first));
        Assert.Equal(regionId, labels.GetRegion(second));
    }

    [Fact]
    public void ApplyMutationIntent_RemoveAssignmentClearsLabelAndUndoRestoresIt()
    {
        var model = CreateModel();
        var labels = CreateLabels();
        var position = new Point3(2, 0, 0);
        var regionId = new RegionId("arm");
        model.SetVoxel(position, 3);
        labels.AddOrUpdateRegion(new RegionDef { Id = regionId, Name = "arm" });
        labels.AssignRegion(regionId, [position]);
        var document = new EditorDocumentState(model, labels);
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);
        var intent = new VoxelMutationIntent
        {
            Assignments = [new VoxelAssignment(position, null)],
            Description = "Remove one",
        };

        var result = new VoxelEditingService().ApplyMutationIntent(
            document,
            undoStack,
            events,
            new ApplyVoxelMutationIntentRequest(intent));

        Assert.True(result.Success);
        Assert.Null(model.GetVoxel(position));
        Assert.Null(labels.GetRegion(position));

        undoStack.Undo();

        Assert.Equal((byte)3, model.GetVoxel(position));
        Assert.Equal(regionId, labels.GetRegion(position));
    }

    [Fact]
    public void RemoveVoxels_RemovesSelectionAsSingleUndoableOperation()
    {
        var model = CreateModel();
        var first = new Point3(0, 0, 0);
        var second = new Point3(1, 0, 0);
        model.SetVoxel(first, 1);
        model.SetVoxel(second, 2);
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new VoxelEditingService().RemoveVoxels(
            document,
            undoStack,
            events,
            new RemoveVoxelsRequest([first, second], "Delete 2 voxels"));

        Assert.True(result.Success);
        Assert.Equal(0, model.GetVoxelCount());

        undoStack.Undo();

        Assert.Equal((byte)1, model.GetVoxel(first));
        Assert.Equal((byte)2, model.GetVoxel(second));
    }

    [Fact]
    public void FloodFill_FillsConnectedSamePaletteOnlyThroughService()
    {
        var model = CreateModel();
        var start = new Point3(0, 0, 0);
        var connected = new Point3(1, 0, 0);
        var separate = new Point3(4, 0, 0);
        model.SetVoxel(start, 1);
        model.SetVoxel(connected, 1);
        model.SetVoxel(separate, 1);
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new VoxelEditingService().FloodFill(
            document,
            undoStack,
            events,
            new FloodFillVoxelRequest(start, 3));

        Assert.True(result.Success);
        Assert.Equal((byte)3, model.GetVoxel(start));
        Assert.Equal((byte)3, model.GetVoxel(connected));
        Assert.Equal((byte)1, model.GetVoxel(separate));

        undoStack.Undo();

        Assert.Equal((byte)1, model.GetVoxel(start));
        Assert.Equal((byte)1, model.GetVoxel(connected));
        Assert.Equal((byte)1, model.GetVoxel(separate));
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
