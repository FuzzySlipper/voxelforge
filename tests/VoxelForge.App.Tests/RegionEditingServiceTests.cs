using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class RegionEditingServiceTests
{
    [Fact]
    public void AssignVoxel_ToAir_FailsWithoutCreatingRegionOrUndoEntry()
    {
        var document = new EditorDocumentState(CreateModel(), CreateLabels());
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new RegionEditingService().AssignVoxel(
            document,
            undoStack,
            events,
            new AssignVoxelRegionRequest("body", new Point3(0, 0, 0)));

        Assert.False(result.Success);
        Assert.Empty(document.Labels.Regions);
        Assert.False(undoStack.CanUndo);
    }

    [Fact]
    public void AssignVoxel_CreatesImplicitRegionAndUndoRemovesIt()
    {
        var model = CreateModel();
        var position = new Point3(0, 0, 0);
        model.SetVoxel(position, 1);
        var document = new EditorDocumentState(model, CreateLabels());
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new RegionEditingService().AssignVoxel(
            document,
            undoStack,
            events,
            new AssignVoxelRegionRequest("body", position));

        Assert.True(result.Success);
        Assert.True(document.Labels.Regions.ContainsKey(new RegionId("body")));
        Assert.Equal(new RegionId("body"), document.Labels.GetRegion(position));

        undoStack.Undo();

        Assert.Empty(document.Labels.Regions);
        Assert.Null(document.Labels.GetRegion(position));
    }

    [Fact]
    public void CreateRegion_IsUndoable()
    {
        var labels = CreateLabels();
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new RegionEditingService().CreateRegion(
            labels,
            undoStack,
            events,
            new CreateRegionRequest("body"));

        Assert.True(result.Success);
        Assert.True(labels.Regions.ContainsKey(new RegionId("body")));

        undoStack.Undo();

        Assert.Empty(labels.Regions);
    }

    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    private static LabelIndex CreateLabels() => new(NullLogger<LabelIndex>.Instance);

    private static UndoStack CreateUndoStack(IEventPublisher events) =>
        new(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);
}
