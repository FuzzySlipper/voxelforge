using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core.Serialization;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class ProjectLifecycleServiceTests
{
    [Fact]
    public void Load_ReplacesDocumentThroughSingleUndoableOperation()
    {
        var oldPosition = new Point3(0, 0, 0);
        var newPosition = new Point3(1, 0, 0);
        var staleLoadedLabelPosition = new Point3(99, 99, 99);

        var document = CreateOldDocument(oldPosition);
        var events = new ApplicationEventDispatcher();
        var undoStack = CreateUndoStack(events);
        string path = WriteLoadedProject(newPosition, staleLoadedLabelPosition);

        try
        {
            var result = new ProjectLifecycleService(NullLoggerFactory.Instance).Load(
                document,
                undoStack,
                events,
                new LoadProjectRequest(path));

            Assert.True(result.Success);
            Assert.True(undoStack.CanUndo);
            AssertLoadedDocument(document, oldPosition, newPosition, staleLoadedLabelPosition);

            undoStack.Undo();

            Assert.Equal((byte)1, document.Model.GetVoxel(oldPosition));
            Assert.Null(document.Model.GetVoxel(newPosition));
            Assert.Equal("Old", document.Model.Palette.Get(1)!.Name);
            Assert.Equal("Stale", document.Model.Palette.Get(9)!.Name);
            Assert.Null(document.Model.Palette.Get(2));
            Assert.Equal(new RegionId("old"), document.Labels.GetRegion(oldPosition));

            undoStack.Redo();

            AssertLoadedDocument(document, oldPosition, newPosition, staleLoadedLabelPosition);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ResolvesBareProjectNameThroughContentDirectory()
    {
        var oldPosition = new Point3(0, 0, 0);
        var newPosition = new Point3(2, 0, 0);
        var staleLoadedLabelPosition = new Point3(88, 88, 88);
        string projectName = $"load-resolves-{Guid.NewGuid():N}";
        string path = Path.Combine("content", projectName + ".vforge");
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        WriteLoadedProject(newPosition, staleLoadedLabelPosition, path);

        try
        {
            var document = CreateOldDocument(oldPosition);
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);

            var result = new ProjectLifecycleService(NullLoggerFactory.Instance).Load(
                document,
                undoStack,
                events,
                new LoadProjectRequest(projectName));

            Assert.True(result.Success);
            AssertLoadedDocument(document, oldPosition, newPosition, staleLoadedLabelPosition);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EditorDocumentState CreateOldDocument(Point3 oldPosition)
    {
        var model = CreateModel();
        model.GridHint = 16;
        model.SetVoxel(oldPosition, 1);
        model.Palette.Set(1, new MaterialDef { Name = "Old", Color = new RgbaColor(1, 2, 3) });
        model.Palette.Set(9, new MaterialDef { Name = "Stale", Color = new RgbaColor(9, 9, 9) });

        var labels = CreateLabels();
        var oldRegion = new RegionId("old");
        labels.AddOrUpdateRegion(new RegionDef { Id = oldRegion, Name = "old" });
        labels.AssignRegion(oldRegion, [oldPosition]);

        return new EditorDocumentState(model, labels);
    }

    private static void AssertLoadedDocument(
        EditorDocumentState document,
        Point3 oldPosition,
        Point3 newPosition,
        Point3 staleLoadedLabelPosition)
    {
        Assert.Null(document.Model.GetVoxel(oldPosition));
        Assert.Equal((byte)2, document.Model.GetVoxel(newPosition));
        Assert.Null(document.Model.Palette.Get(1));
        Assert.Null(document.Model.Palette.Get(9));
        Assert.Equal("Loaded", document.Model.Palette.Get(2)!.Name);
        Assert.Null(document.Labels.GetRegion(staleLoadedLabelPosition));
        Assert.Equal(new RegionId("loaded"), document.Labels.GetRegion(newPosition));
        Assert.Single(document.Labels.GetVoxelsInRegion(new RegionId("loaded")));
    }

    private static string WriteLoadedProject(Point3 newPosition, Point3 staleLoadedLabelPosition)
    {
        string path = Path.Combine(Path.GetTempPath(), $"voxelforge-load-{Guid.NewGuid():N}.vforge");
        WriteLoadedProject(newPosition, staleLoadedLabelPosition, path);
        return path;
    }

    private static void WriteLoadedProject(Point3 newPosition, Point3 staleLoadedLabelPosition, string path)
    {
        var model = CreateModel();
        model.GridHint = 64;
        model.SetVoxel(newPosition, 2);
        model.Palette.Set(2, new MaterialDef { Name = "Loaded", Color = new RgbaColor(20, 30, 40) });

        var labels = CreateLabels();
        var loadedRegion = new RegionId("loaded");
        labels.AddOrUpdateRegion(new RegionDef { Id = loadedRegion, Name = "loaded" });
        labels.AssignRegion(loadedRegion, [newPosition, staleLoadedLabelPosition]);

        var serializer = new ProjectSerializer(NullLoggerFactory.Instance);
        string json = serializer.Serialize(model, labels, [], new ProjectMetadata { Name = "loaded-project" });
        File.WriteAllText(path, json);
    }

    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    private static LabelIndex CreateLabels() => new(NullLogger<LabelIndex>.Instance);

    private static UndoStack CreateUndoStack(IEventPublisher events) =>
        new(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);
}
