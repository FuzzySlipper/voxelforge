using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Tests;

public sealed class EditorSnapshotServiceTests
{
    private static VoxelModel CreateModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        return model;
    }

    private static LabelIndex CreateLabels()
    {
        return new LabelIndex(NullLogger<LabelIndex>.Instance);
    }

    private static UndoHistoryState CreateUndoHistory()
    {
        return new UndoHistoryState(100);
    }

    private static EditorSnapshotService CreateService()
    {
        return new EditorSnapshotService(
            new MeshSnapshotService(new GreedyMesher()),
            new PaletteSnapshotService());
    }

    [Fact]
    public void BuildSnapshot_ProducesAllSubSnapshots()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var document = new EditorDocumentState(model, CreateLabels());
        var session = new EditorSessionState();
        var undoHistory = CreateUndoHistory();

        var service = CreateService();
        var snapshot = service.BuildSnapshot(document, session, undoHistory);

        Assert.NotNull(snapshot.Document);
        Assert.NotNull(snapshot.Session);
        Assert.NotNull(snapshot.Palette);
        Assert.NotNull(snapshot.Mesh);
        Assert.NotNull(snapshot.Labels);
        Assert.NotNull(snapshot.UndoHistory);
        Assert.NotNull(snapshot.Diagnostics);
    }

    [Fact]
    public void BuildSnapshot_EmptyModel_HasCorrectDocumentFields()
    {
        var model = CreateModel();
        var document = new EditorDocumentState(model, CreateLabels());
        var session = new EditorSessionState();
        var undoHistory = CreateUndoHistory();

        var docSnapshot = EditorSnapshotService.BuildDocumentSnapshot(document, false, "test");

        Assert.Equal("test", docSnapshot.ModelId);
        Assert.Equal(0, docSnapshot.VoxelCount);
        Assert.Null(docSnapshot.Bounds);
        Assert.False(docSnapshot.IsDirty);
    }

    [Fact]
    public void BuildDocumentSnapshot_WithVoxels_HasBounds()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(3, 5, 7), 1);
        var document = new EditorDocumentState(model, CreateLabels());

        var docSnapshot = EditorSnapshotService.BuildDocumentSnapshot(document);

        Assert.Equal(1, docSnapshot.VoxelCount);
        Assert.NotNull(docSnapshot.Bounds);
        Assert.Equal(3, docSnapshot.Bounds!.MinX);
        Assert.Equal(7, docSnapshot.Bounds.MaxZ);
    }

    [Fact]
    public void BuildSessionSnapshot_ReflectsSessionState()
    {
        var model = CreateModel();
        var document = new EditorDocumentState(model, CreateLabels());
        var session = new EditorSessionState
        {
            ActivePaletteIndex = 3,
            ActiveTool = EditorTool.Paint,
            ActiveFrameIndex = 0,
        };
        session.SelectedVoxels.Add(new Point3(1, 2, 3));

        var sessionSnapshot = EditorSnapshotService.BuildSessionSnapshot(document, session);

        Assert.Equal("Paint", sessionSnapshot.ActiveTool);
        Assert.Equal((byte)3, sessionSnapshot.ActivePaletteIndex);
        Assert.Equal(0, sessionSnapshot.ActiveFrameIndex);
        Assert.Single(sessionSnapshot.SelectedVoxels);
        Assert.Equal(new Point3Snapshot(1, 2, 3), sessionSnapshot.SelectedVoxels[0]);
    }

    [Fact]
    public void BuildSelectionSnapshot_ComputesBounds()
    {
        var session = new EditorSessionState();
        session.SelectedVoxels.Add(new Point3(1, 2, 3));
        session.SelectedVoxels.Add(new Point3(5, 5, 5));

        var selection = EditorSnapshotService.BuildSelectionSnapshot(session);

        Assert.Equal(2, selection.Voxels.Count);
        Assert.NotNull(selection.Bounds);
        Assert.Equal(1, selection.Bounds!.MinX);
        Assert.Equal(5, selection.Bounds.MaxX);
    }

    [Fact]
    public void BuildSelectionSnapshot_EmptySelection_NoBounds()
    {
        var session = new EditorSessionState();

        var selection = EditorSnapshotService.BuildSelectionSnapshot(session);

        Assert.Empty(selection.Voxels);
        Assert.Null(selection.Bounds);
    }

    [Fact]
    public void BuildSelectionSnapshot_WithRegion()
    {
        var session = new EditorSessionState { ActiveRegion = new RegionId("body") };

        var selection = EditorSnapshotService.BuildSelectionSnapshot(session);

        Assert.Equal("body", selection.ActiveRegionId);
    }

    [Fact]
    public void BuildUndoHistorySnapshot_ReflectsState()
    {
        var undoHistory = new UndoHistoryState(100);
        var events = new ApplicationEventDispatcher();
        var undoStack = new UndoStack(undoHistory, NullLogger<UndoStack>.Instance, events);

        var model = CreateModel();
        var document = new EditorDocumentState(model, CreateLabels());

        // Execute a command so undo history has content
        undoStack.Execute(new SetVoxelCommand(document.Model, new Point3(0, 0, 0), 1));

        var snapshot = EditorSnapshotService.BuildUndoHistorySnapshot(undoHistory);

        Assert.True(snapshot.CanUndo);
        Assert.False(snapshot.CanRedo);
        Assert.Equal(1, snapshot.UndoDepth);
        Assert.Contains("Set voxel at", snapshot.LastCommandDescription);
    }

    [Fact]
    public void BuildUndoHistorySnapshot_Empty_NoUndoRedo()
    {
        var undoHistory = new UndoHistoryState(100);

        var snapshot = EditorSnapshotService.BuildUndoHistorySnapshot(undoHistory);

        Assert.False(snapshot.CanUndo);
        Assert.False(snapshot.CanRedo);
        Assert.Equal(0, snapshot.UndoDepth);
        Assert.Null(snapshot.LastCommandDescription);
    }

    [Fact]
    public void BuildLabelsSnapshot_WithRegions()
    {
        var labels = CreateLabels();
        var regionId = new RegionId("head");
        labels.AddOrUpdateRegion(new RegionDef
        {
            Id = regionId,
            Name = "Head",
            Voxels = { new Point3(0, 0, 0), new Point3(1, 0, 0) },
        });

        var snapshot = EditorSnapshotService.BuildLabelsSnapshot(labels);

        Assert.Single(snapshot.Regions);
        Assert.Equal("head", snapshot.Regions[0].RegionId);
        Assert.Equal("Head", snapshot.Regions[0].Name);
        Assert.Equal(2, snapshot.Regions[0].VoxelCount);
        Assert.Equal(2, snapshot.LabeledVoxelCount);
    }

    [Fact]
    public void BuildLabelsSnapshot_Empty()
    {
        var labels = CreateLabels();

        var snapshot = EditorSnapshotService.BuildLabelsSnapshot(labels);

        Assert.Empty(snapshot.Regions);
        Assert.Equal(0, snapshot.LabeledVoxelCount);
    }

    [Fact]
    public void BuildDiagnosticsSnapshot_Defaults()
    {
        var snapshot = EditorSnapshotService.BuildDiagnosticsSnapshot();

        Assert.NotEqual(default, snapshot.Timestamp);
        Assert.Empty(snapshot.RecentStatuses);
    }

    [Fact]
    public void BuildDiagnosticsSnapshot_WithStatuses()
    {
        var statuses = new List<StatusEntrySnapshot>
        {
            new() { Severity = "info", Source = "mesh", Message = "Mesh generated" },
            new() { Severity = "warning", Source = "persistence", Message = "File locked" },
        };

        var snapshot = EditorSnapshotService.BuildDiagnosticsSnapshot(statuses);

        Assert.Equal(2, snapshot.RecentStatuses.Count);
        Assert.Equal("info", snapshot.RecentStatuses[0].Severity);
        Assert.Equal("warning", snapshot.RecentStatuses[1].Severity);
    }

    [Fact]
    public void ToStatusEntry_ConvertsEditorStatusEvent()
    {
        var statusEvent = new EditorStatusEvent("mesh_service", EditorStatusSeverity.Warning, "Large mesh detected");

        var entry = EditorSnapshotService.ToStatusEntry(statusEvent);

        Assert.Equal("warning", entry.Severity);
        Assert.Equal("mesh_service", entry.Source);
        Assert.Equal("Large mesh detected", entry.Message);
    }

    [Fact]
    public void FullSnapshot_MeshAndPaletteAreConsistent()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var document = new EditorDocumentState(model, CreateLabels());
        var session = new EditorSessionState();
        var undoHistory = CreateUndoHistory();

        var service = CreateService();
        var snapshot = service.BuildSnapshot(document, session, undoHistory);

        // The palette snapshot should have an entry for index 1 (Stone)
        Assert.Single(snapshot.Palette.Entries);
        Assert.Equal((byte)1, snapshot.Palette.Entries[0].Index);

        // The mesh snapshot should have vertices with non-zero data
        Assert.True(snapshot.Mesh.VertexCount > 0);
        Assert.True(snapshot.Mesh.TriangleCount > 0);
    }

    [Fact]
    public void FullSnapshot_WithTwoMaterials_BothAppearInPalette()
    {
        var model = CreateModel();
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0) });
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(2, 0, 0), 2);

        var document = new EditorDocumentState(model, CreateLabels());
        var session = new EditorSessionState();
        var undoHistory = CreateUndoHistory();

        var service = CreateService();
        var snapshot = service.BuildSnapshot(document, session, undoHistory);

        Assert.Equal(2, snapshot.Palette.EntryCount);
    }
}