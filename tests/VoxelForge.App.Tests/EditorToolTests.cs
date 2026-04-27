using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.App.Tools;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class EditorToolTests
{
    [Fact]
    public void FillTool_PublishesStatusEventWhenFloodFillFails()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        var state = new EditorState(new EditorDocumentState(model, labels), new EditorSessionState());
        var events = new ApplicationEventDispatcher();
        var statusHandler = new RecordingEditorStatusHandler();
        events.Register<EditorStatusEvent>(statusHandler);
        var undoStack = new UndoStack(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);
        var tool = new FillTool(new VoxelEditingService());

        tool.OnMouseDown(
            new RaycastHit(new Point3(0, 0, 0), new Point3(0, 1, 0), 0),
            state,
            undoStack,
            events);

        Assert.Equal(0, model.GetVoxelCount());
        var statusEvent = Assert.Single(statusHandler.Events);
        Assert.Equal("fill", statusEvent.Source);
        Assert.Equal(EditorStatusSeverity.Warning, statusEvent.Severity);
        Assert.Contains("Cannot flood fill air", statusEvent.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingEditorStatusHandler : IEventHandler<EditorStatusEvent>
    {
        public List<EditorStatusEvent> Events { get; } = [];

        public void Handle(EditorStatusEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
