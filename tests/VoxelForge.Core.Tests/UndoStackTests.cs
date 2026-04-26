using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;

namespace VoxelForge.Core.Tests;

public sealed class UndoStackTests
{
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);
    private static LabelIndex CreateIndex() => new(NullLogger<LabelIndex>.Instance);
    private static UndoStack CreateStack(int maxDepth = 100, IEventPublisher? events = null) =>
        new(new UndoHistoryState(maxDepth), NullLogger<UndoStack>.Instance,
            events ?? new ApplicationEventDispatcher());

    [Fact]
    public void SetVoxel_UndoThreeTimes_ModelEmpty()
    {
        var model = CreateModel();
        var stack = CreateStack();

        stack.Execute(new SetVoxelCommand(model, new Point3(0, 0, 0), 1));
        stack.Execute(new SetVoxelCommand(model, new Point3(1, 0, 0), 1));
        stack.Execute(new SetVoxelCommand(model, new Point3(2, 0, 0), 1));

        Assert.Equal(3, model.GetVoxelCount());

        stack.Undo();
        stack.Undo();
        stack.Undo();

        Assert.Equal(0, model.GetVoxelCount());
    }

    [Fact]
    public void UndoThenRedo_RestoresVoxel()
    {
        var model = CreateModel();
        var stack = CreateStack();

        stack.Execute(new SetVoxelCommand(model, new Point3(5, 5, 5), 3));
        Assert.Equal((byte)3, model.GetVoxel(new Point3(5, 5, 5)));

        stack.Undo();
        Assert.Null(model.GetVoxel(new Point3(5, 5, 5)));

        stack.Redo();
        Assert.Equal((byte)3, model.GetVoxel(new Point3(5, 5, 5)));
    }

    [Fact]
    public void SetVoxel_OverwriteAndUndo_RestoresOldValue()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var stack = CreateStack();

        stack.Execute(new SetVoxelCommand(model, new Point3(0, 0, 0), 5));
        Assert.Equal((byte)5, model.GetVoxel(new Point3(0, 0, 0)));

        stack.Undo();
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public void RemoveVoxel_UndoRestoresVoxel()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 7);
        var stack = CreateStack();

        stack.Execute(new RemoveVoxelCommand(model, new Point3(0, 0, 0)));
        Assert.Null(model.GetVoxel(new Point3(0, 0, 0)));

        stack.Undo();
        Assert.Equal((byte)7, model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public void FillRegion_UndoRemovesAll()
    {
        var model = CreateModel();
        var stack = CreateStack();

        stack.Execute(new FillRegionCommand(model, new Point3(0, 0, 0), new Point3(9, 9, 9), 1));
        Assert.Equal(1000, model.GetVoxelCount());

        stack.Undo();
        Assert.Equal(0, model.GetVoxelCount());
    }

    [Fact]
    public void FillRegion_UndoPreservesExistingVoxels()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 5, 5), 9);
        var stack = CreateStack();

        stack.Execute(new FillRegionCommand(model, new Point3(0, 0, 0), new Point3(9, 9, 9), 1));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(5, 5, 5)));

        stack.Undo();
        Assert.Equal((byte)9, model.GetVoxel(new Point3(5, 5, 5)));
    }

    [Fact]
    public void MaxDepth_OldestCommandDropped()
    {
        var model = CreateModel();
        var stack = CreateStack(maxDepth: 2);

        stack.Execute(new SetVoxelCommand(model, new Point3(0, 0, 0), 1));
        stack.Execute(new SetVoxelCommand(model, new Point3(1, 0, 0), 1));
        stack.Execute(new SetVoxelCommand(model, new Point3(2, 0, 0), 1));

        Assert.True(stack.CanUndo);

        // Can only undo 2 times (oldest was dropped)
        stack.Undo();
        stack.Undo();
        Assert.False(stack.CanUndo);

        // First voxel still exists (its command was dropped)
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public void CompoundCommand_ExecutesAll()
    {
        var model = CreateModel();
        var stack = CreateStack();

        var commands = Enumerable.Range(0, 10)
            .Select(i => (IEditorCommand)new SetVoxelCommand(model, new Point3(i, 0, 0), 1))
            .ToList();

        stack.Execute(new CompoundCommand(commands, "Place 10 voxels"));

        Assert.Equal(10, model.GetVoxelCount());
    }

    [Fact]
    public void CompoundCommand_UndoReversesAll()
    {
        var model = CreateModel();
        var stack = CreateStack();

        var commands = Enumerable.Range(0, 10)
            .Select(i => (IEditorCommand)new SetVoxelCommand(model, new Point3(i, 0, 0), 1))
            .ToList();

        stack.Execute(new CompoundCommand(commands, "Place 10 voxels"));
        stack.Undo();

        Assert.Equal(0, model.GetVoxelCount());
    }

    [Fact]
    public void CompoundCommand_50Commands_SingleUndo()
    {
        var model = CreateModel();
        var stack = CreateStack();

        var commands = Enumerable.Range(0, 50)
            .Select(i => (IEditorCommand)new SetVoxelCommand(model, new Point3(i, 0, 0), 1))
            .ToList();

        stack.Execute(new CompoundCommand(commands, "LLM generated 50 voxels"));
        Assert.Equal(50, model.GetVoxelCount());

        stack.Undo();
        Assert.Equal(0, model.GetVoxelCount());
    }

    [Fact]
    public void PaintCommand_OnlyAffectsExistingVoxels()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var stack = CreateStack();

        stack.Execute(new PaintVoxelCommand(model, new Point3(0, 0, 0), 5));
        Assert.Equal((byte)5, model.GetVoxel(new Point3(0, 0, 0)));

        // Paint on air does nothing
        stack.Execute(new PaintVoxelCommand(model, new Point3(99, 99, 99), 5));
        Assert.Null(model.GetVoxel(new Point3(99, 99, 99)));

        stack.Undo(); // undo paint on air
        stack.Undo(); // undo paint on existing
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public void AssignLabel_UndoRestoresPrevious()
    {
        var labels = CreateIndex();
        labels.AddOrUpdateRegion(new RegionDef { Id = new RegionId("arm"), Name = "arm" });
        labels.AddOrUpdateRegion(new RegionDef { Id = new RegionId("leg"), Name = "leg" });

        var pos = new Point3(0, 0, 0);
        labels.AssignRegion(new RegionId("arm"), [pos]);

        var stack = CreateStack();
        stack.Execute(new AssignLabelCommand(labels, new RegionId("leg"), [pos]));
        Assert.Equal(new RegionId("leg"), labels.GetRegion(pos));

        stack.Undo();
        Assert.Equal(new RegionId("arm"), labels.GetRegion(pos));
    }

    [Fact]
    public void UndoHistoryChangedEvent_PublishesOnExecuteUndoRedo()
    {
        var model = CreateModel();
        var events = new ApplicationEventDispatcher();
        var handler = new RecordingUndoHistoryHandler();
        events.Register<UndoHistoryChangedEvent>(handler);
        var stack = CreateStack(events: events);

        stack.Execute(new SetVoxelCommand(model, new Point3(0, 0, 0), 1));
        Assert.Single(handler.Events);
        Assert.Equal(UndoHistoryChangeKind.Executed, handler.Events[0].Kind);
        Assert.True(handler.Events[0].CanUndo);
        Assert.False(handler.Events[0].CanRedo);

        stack.Undo();
        Assert.Equal(2, handler.Events.Count);
        Assert.Equal(UndoHistoryChangeKind.Undone, handler.Events[1].Kind);
        Assert.False(handler.Events[1].CanUndo);
        Assert.True(handler.Events[1].CanRedo);

        stack.Redo();
        Assert.Equal(3, handler.Events.Count);
        Assert.Equal(UndoHistoryChangeKind.Redone, handler.Events[2].Kind);
        Assert.True(handler.Events[2].CanUndo);
        Assert.False(handler.Events[2].CanRedo);
    }

    private sealed class RecordingUndoHistoryHandler : IEventHandler<UndoHistoryChangedEvent>
    {
        public List<UndoHistoryChangedEvent> Events { get; } = [];

        public void Handle(UndoHistoryChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }

    [Fact]
    public void Execute_ClearsRedoStack()
    {
        var model = CreateModel();
        var stack = CreateStack();

        stack.Execute(new SetVoxelCommand(model, new Point3(0, 0, 0), 1));
        stack.Undo();
        Assert.True(stack.CanRedo);

        stack.Execute(new SetVoxelCommand(model, new Point3(1, 0, 0), 2));
        Assert.False(stack.CanRedo);
    }
}
