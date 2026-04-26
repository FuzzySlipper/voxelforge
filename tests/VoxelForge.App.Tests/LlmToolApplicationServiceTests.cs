using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class LlmToolApplicationServiceTests
{
    [Fact]
    public void ApplyMutationIntents_UsesVoxelEditingServiceAndUndoHistory()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        var document = new EditorDocumentState(model, new LabelIndex(NullLogger<LabelIndex>.Instance));
        var events = new ApplicationEventDispatcher();
        var undoStack = new UndoStack(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);
        var service = new LlmToolApplicationService(new VoxelEditingService());
        var intent = new VoxelMutationIntent
        {
            Assignments = [new VoxelAssignment(new Point3(0, 0, 0), 1)],
            Description = "LLM set one voxel",
        };

        var result = service.ApplyMutationIntents(
            document,
            undoStack,
            events,
            new ApplyLlmMutationIntentsRequest([intent]));

        Assert.True(result.Success);
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.True(undoStack.CanUndo);

        undoStack.Undo();

        Assert.Null(model.GetVoxel(new Point3(0, 0, 0)));
    }
}
