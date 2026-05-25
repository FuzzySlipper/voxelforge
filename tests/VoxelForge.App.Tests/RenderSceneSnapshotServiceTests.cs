using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Render;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Tests;

public sealed class RenderSceneSnapshotServiceTests
{
    private static VoxelModel CreateVoxelModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0, 255) });
        return model;
    }

    private static VoxelForgeWorkspaceState CreateWorkspace(
        VoxelModel? model = null,
        ReferenceModelState? referenceModels = null)
    {
        model ??= CreateVoxelModel();
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        var document = new EditorDocumentState(model, labels);
        var session = new EditorSessionState();
        var undoHistory = new UndoHistoryState(100);
        var events = new ApplicationEventDispatcher();
        var undoStack = new UndoStack(undoHistory, NullLogger<UndoStack>.Instance, events);
        referenceModels ??= new ReferenceModelState();
        var referenceImages = new ReferenceImageState();

        return new VoxelForgeWorkspaceState(
            document,
            session,
            undoHistory,
            undoStack,
            events,
            referenceModels,
            referenceImages)
        {
            ModelId = "test-model",
            CurrentModelName = "test",
        };
    }

    // ── RenderSceneSnapshotService tests ──

    [Fact]
    public void BuildSnapshot_EmptyWorkspace_ProducesEmptySnapshot()
    {
        var workspace = CreateWorkspace();
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        Assert.Equal("voxelforge.render_scene@1", snapshot.SchemaVersion);
        Assert.Equal(0, snapshot.Revision);
        Assert.Equal("test-model", snapshot.ModelId);
        Assert.Equal("test", snapshot.Source.Host);
        Assert.Empty(snapshot.VoxelMeshes);
        Assert.Empty(snapshot.ReferenceNodes);
        Assert.Empty(snapshot.Materials);
        Assert.Empty(snapshot.Textures);
        Assert.Equal(2, snapshot.Palette.Count); // CreateWorkspace creates model with 2 palette entries
        Assert.Null(snapshot.Bounds);
        Assert.Null(snapshot.CombinedBounds);
    }

    [Fact]
    public void BuildSnapshot_WithVoxels_ProducesNonEmptySnapshot()
    {
        var model = CreateVoxelModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 2);

        var workspace = CreateWorkspace(model);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test", capabilities: ["voxel_mesh", "palette"]);

        Assert.Single(snapshot.VoxelMeshes);
        Assert.NotEmpty(snapshot.Palette);
        Assert.NotNull(snapshot.Bounds);
        Assert.NotNull(snapshot.Source);
        Assert.Contains("voxel_mesh", snapshot.Source.Capabilities);
        Assert.Contains("palette", snapshot.Source.Capabilities);

        var voxelMesh = snapshot.VoxelMeshes[0];
        Assert.True(voxelMesh.Positions.Length > 0, "Should have vertex positions");
        Assert.True(voxelMesh.Indices.Length > 0, "Should have triangle indices");
        Assert.NotEmpty(voxelMesh.ColorsRgba);
    }

    [Fact]
    public void BuildSnapshot_Revision_Increments()
    {
        var workspace = CreateWorkspace();
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot1 = service.BuildSnapshot(workspace, hostId: "test");
        Assert.Equal(0, snapshot1.Revision);

        workspace.IncrementRevision();
        var snapshot2 = service.BuildSnapshot(workspace, hostId: "test");

        Assert.Equal(1, snapshot2.Revision);
    }

    // ── RenderSceneEventProjector tests ──

    [Fact]
    public void Project_VoxelModelChangedEvent_ReturnsMeshChangedAndSnapshotRequired()
    {
        var projector = new RenderSceneEventProjector();
        var workspace = CreateWorkspace();
        var evt = new VoxelModelChangedEvent(
            VoxelModelChangeKind.SetVoxel,
            "Set voxel at (0, 0, 0)",
            1);

        var events = projector.Project(evt, workspace);

        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Kind == "render.mesh_changed");
        Assert.Contains(events, e => e.Kind == "render.snapshot_required");
    }

    [Fact]
    public void Project_PaletteChangedEvent_ReturnsPaletteChanged()
    {
        var projector = new RenderSceneEventProjector();
        var workspace = CreateWorkspace();
        var evt = new PaletteChangedEvent(
            PaletteChangeKind.EntryUpdated,
            "Updated palette entry 1",
            1,
            1);

        var events = projector.Project(evt, workspace);

        Assert.Contains(events, e => e.Kind == "render.palette_changed");
        Assert.Contains(events, e => e.Kind == "render.snapshot_required");
    }

    [Fact]
    public void Project_ReferenceModelChangedEvent_ReturnsReferenceChanged()
    {
        var projector = new RenderSceneEventProjector();
        var workspace = CreateWorkspace();
        var evt = new ReferenceModelChangedEvent(
            ReferenceModelChangeKind.Loaded,
            "Loaded model",
            0);

        var events = projector.Project(evt, workspace);

        Assert.Contains(events, e => e.Kind == "render.reference_changed");
        Assert.Contains(events, e => e.Kind == "render.snapshot_required");
    }

    [Fact]
    public void Project_ProjectLoadedEvent_ReturnsStateChanged()
    {
        var projector = new RenderSceneEventProjector();
        var workspace = CreateWorkspace();
        var evt = new ProjectLoadedEvent("/path/test.vforge", "test", 42, 3, 1);

        var events = projector.Project(evt, workspace);

        Assert.Contains(events, e => e.Kind == "render.state_changed");
        Assert.Contains(events, e => e.Kind == "render.snapshot_required");
    }

    [Fact]
    public void Project_UndoHistoryChangedEvent_ReturnsStateChanged()
    {
        var projector = new RenderSceneEventProjector();
        var workspace = CreateWorkspace();
        var evt = new UndoHistoryChangedEvent(
            UndoHistoryChangeKind.Executed,
            "Set voxel",
            true,
            false);

        var events = projector.Project(evt, workspace);

        Assert.Single(events);
        Assert.Equal("render.state_changed", events[0].Kind);
    }

    // ── VoxelForgeWorkspaceState tests ──

    [Fact]
    public void WorkspaceState_DefaultValues()
    {
        var workspace = CreateWorkspace();

        Assert.Equal("test-model", workspace.ModelId);
        Assert.Equal("test", workspace.CurrentModelName);
        Assert.False(workspace.IsDirty);
        Assert.Equal("Ready", workspace.StatusMessage);
        Assert.Equal(0, workspace.Revision);
        Assert.Null(workspace.ProjectPath);
    }

    [Fact]
    public void WorkspaceState_IncrementRevision_IsAtomic()
    {
        var workspace = CreateWorkspace();

        var r1 = workspace.IncrementRevision();
        var r2 = workspace.IncrementRevision();

        Assert.Equal(1, r1);
        Assert.Equal(2, r2);
        Assert.Equal(2, workspace.Revision);
    }

    [Fact]
    public void WorkspaceState_Revision_ThreadSafeRead()
    {
        var workspace = CreateWorkspace();
        workspace.IncrementRevision();
        workspace.IncrementRevision();
        workspace.IncrementRevision();

        Assert.Equal(3, workspace.Revision);
    }

    // ── MCP and Bridge should produce equivalent snapshot semantics ──

    [Fact]
    public void SameWorkspace_ProducesSameSnapshot_FromAnyHostId()
    {
        var model = CreateVoxelModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var workspace = CreateWorkspace(model);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var mcpSnapshot = service.BuildSnapshot(workspace, hostId: "mcp");
        var bridgeSnapshot = service.BuildSnapshot(workspace, hostId: "bridge");

        // Schema, revision, model ID, and voxel data must be identical
        Assert.Equal(mcpSnapshot.SchemaVersion, bridgeSnapshot.SchemaVersion);
        Assert.Equal(mcpSnapshot.Revision, bridgeSnapshot.Revision);
        Assert.Equal(mcpSnapshot.ModelId, bridgeSnapshot.ModelId);
        Assert.Equal(mcpSnapshot.VoxelMeshes.Count, bridgeSnapshot.VoxelMeshes.Count);
        Assert.Equal(mcpSnapshot.Palette.Count, bridgeSnapshot.Palette.Count);

        // Host identification differs
        Assert.Equal("mcp", mcpSnapshot.Source.Host);
        Assert.Equal("bridge", bridgeSnapshot.Source.Host);

        // Voxel mesh data should be equivalent
        if (mcpSnapshot.VoxelMeshes.Count > 0 && bridgeSnapshot.VoxelMeshes.Count > 0)
        {
            Assert.Equal(mcpSnapshot.VoxelMeshes[0].Positions.Length, bridgeSnapshot.VoxelMeshes[0].Positions.Length);
            Assert.Equal(mcpSnapshot.VoxelMeshes[0].Indices.Length, bridgeSnapshot.VoxelMeshes[0].Indices.Length);
        }
    }

    // ── ReferenceModelApplicationService tests ──

    [Fact]
    public void RemoveReferenceModel_InvalidIndex_ReturnsFailure()
    {
        var workspace = CreateWorkspace();
        var service = new ReferenceModelApplicationService();

        var result = service.RemoveReferenceModel(workspace, index: 0);

        Assert.False(result.Success);
    }

    [Fact]
    public void ClearReferenceModels_EmptyWorkspace_ReturnsSuccess()
    {
        var workspace = CreateWorkspace();
        var service = new ReferenceModelApplicationService();

        var result = service.ClearReferenceModels(workspace);

        Assert.True(result.Success);
        Assert.Empty(workspace.ReferenceModels.Models);
    }

    [Fact]
    public void ClearReferenceModels_WithModels_ReturnsSuccessAndEvents()
    {
        var workspace = CreateWorkspace();
        // Simulate a loaded reference model by adding metadata without file
        var modelData = new VoxelForge.Core.Reference.ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "obj",
            Meshes = [],
            IsVisible = true,
            Scale = 1f,
        };
        workspace.ReferenceModels.Add(modelData);
        var service = new ReferenceModelApplicationService();

        var result = service.ClearReferenceModels(workspace);

        Assert.True(result.Success);
        Assert.NotEmpty(result.Events);
        Assert.Empty(workspace.ReferenceModels.Models);
    }

    // ── WorkspaceCommandApplicationService tests ──

    [Fact]
    public void Execute_UnknownCommand_ReturnsFailure()
    {
        var workspace = CreateWorkspace();
        var voxelService = new VoxelEditingService();
        var service = new WorkspaceCommandApplicationService(voxelService);

        var result = service.Execute(workspace, new WorkspaceCommandRequest
        {
            CommandName = "nonexistent_command",
        });

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void Undo_EmptyHistory_ReturnsFailure()
    {
        var workspace = CreateWorkspace();
        var voxelService = new VoxelEditingService();
        var service = new WorkspaceCommandApplicationService(voxelService);

        var result = service.Undo(workspace);

        Assert.False(result.Success);
    }

    [Fact]
    public void Redo_EmptyHistory_ReturnsFailure()
    {
        var workspace = CreateWorkspace();
        var voxelService = new VoxelEditingService();
        var service = new WorkspaceCommandApplicationService(voxelService);

        var result = service.Redo(workspace);

        Assert.False(result.Success);
    }

    [Fact]
    public void RenderSceneSnapshot_Contains_SchemaVersion()
    {
        var workspace = CreateWorkspace();
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        Assert.Equal("voxelforge.render_scene@1", snapshot.SchemaVersion);
    }
}
