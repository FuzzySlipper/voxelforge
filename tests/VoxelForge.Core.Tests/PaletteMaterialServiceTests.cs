using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;

namespace VoxelForge.Core.Tests;

public sealed class PaletteMaterialServiceTests
{
    [Fact]
    public void AddMaterial_NewEntry_IsUndoable()
    {
        var model = CreateModel();
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new PaletteMaterialService().AddMaterial(
            model,
            undoStack,
            events,
            new AddPaletteMaterialRequest(5, "Stone", 10, 20, 30, 255));

        Assert.True(result.Success);
        Assert.Equal("Stone", model.Palette.Get(5)!.Name);

        undoStack.Undo();

        Assert.Null(model.Palette.Get(5));

        undoStack.Redo();

        Assert.Equal("Stone", model.Palette.Get(5)!.Name);
    }

    [Fact]
    public void AddMaterial_ExistingEntry_UndoRestoresPreviousMaterial()
    {
        var model = CreateModel();
        model.Palette.Set(5, new MaterialDef
        {
            Name = "Old",
            Color = new RgbaColor(1, 2, 3),
            Metadata = new Dictionary<string, string> { ["tex_albedo"] = "old.png" },
        });
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new PaletteMaterialService().AddMaterial(
            model,
            undoStack,
            events,
            new AddPaletteMaterialRequest(5, "New", 10, 20, 30, 255));

        Assert.True(result.Success);
        Assert.Equal("New", model.Palette.Get(5)!.Name);

        undoStack.Undo();

        var restored = model.Palette.Get(5)!;
        Assert.Equal("Old", restored.Name);
        Assert.Equal(new RgbaColor(1, 2, 3), restored.Color);
        Assert.Equal("old.png", restored.Metadata["tex_albedo"]);
    }

    [Fact]
    public void AddMaterial_RejectsAirIndex()
    {
        var model = CreateModel();
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);

        var result = new PaletteMaterialService().AddMaterial(
            model,
            undoStack,
            events,
            new AddPaletteMaterialRequest(0, "Air", 0, 0, 0, 0));

        Assert.False(result.Success);
        Assert.Null(model.Palette.Get(0));
        Assert.False(undoStack.CanUndo);
    }

    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    private static UndoStack CreateUndoStack(IEventPublisher events) =>
        new(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);
}
