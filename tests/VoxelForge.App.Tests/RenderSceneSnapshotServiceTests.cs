using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Render;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;
using VoxelForge.Core.Reference;

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

    /// <summary>
    /// Create a simple reference model with one mesh containing a single triangle.
    /// </summary>
    private static ReferenceModelData CreateReferenceModel(
        string filePath = "/fake/models/cube.obj",
        string format = "obj",
        float posX = 0, float posY = 0, float posZ = 0,
        float rotX = 0, float rotY = 0, float rotZ = 0,
        float scale = 1f,
        bool visible = true,
        ReferenceVertex[]? vertices = null,
        int[]? indices = null,
        string materialName = "default")
    {
        vertices ??=
        [
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 255, 0, 0, 255, 1, 0),
            new ReferenceVertex(1, 0, 0, 0, 0, 1, 0, 255, 0, 255, 0, 1),
            new ReferenceVertex(0, 1, 0, 0, 0, 1, 0, 0, 255, 255, 0, 0),
        ];

        indices ??= [0, 1, 2];

        var mesh = new ReferenceMeshData
        {
            Vertices = vertices,
            Indices = indices,
            MaterialName = materialName,
        };

        return new ReferenceModelData
        {
            FilePath = filePath,
            Format = format,
            Meshes = [mesh],
            PositionX = posX,
            PositionY = posY,
            PositionZ = posZ,
            RotationX = rotX,
            RotationY = rotY,
            RotationZ = rotZ,
            Scale = scale,
            IsVisible = visible,
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

    // ── Multi-model / multi-mesh material indexing ──

    [Fact]
    public void BuildSnapshot_MultiModelMultiMesh_MaterialIndicesAreDeterministic()
    {
        // Two models, each with two meshes → 4 materials total.
        // MaterialIndex in primitives must index correctly into snapshot.Materials.
        var model1Mesh1 = new ReferenceMeshData
        {
            Vertices =
            [
                new(0, 0, 0, 0, 0, 1, 255, 255, 255, 255),
                new(1, 0, 0, 0, 0, 1, 255, 255, 255, 255),
                new(0, 1, 0, 0, 0, 1, 255, 255, 255, 255),
            ],
            Indices = [0, 1, 2],
            MaterialName = "Model1_Mesh1",
        };
        var model1Mesh2 = new ReferenceMeshData
        {
            Vertices =
            [
                new(0, 0, 0, 0, 0, 1, 128, 128, 128, 255),
                new(1, 0, 0, 0, 0, 1, 128, 128, 128, 255),
                new(0, 1, 0, 0, 0, 1, 128, 128, 128, 255),
            ],
            Indices = [0, 1, 2],
            MaterialName = "Model1_Mesh2",
        };
        var model1 = new ReferenceModelData
        {
            FilePath = "/fake/model1.obj",
            Format = "obj",
            Meshes = [model1Mesh1, model1Mesh2],
            IsVisible = true,
            Scale = 1f,
        };

        var model2Mesh1 = new ReferenceMeshData
        {
            Vertices =
            [
                new(0, 0, 0, 0, 0, 1, 64, 64, 64, 255),
                new(1, 0, 0, 0, 0, 1, 64, 64, 64, 255),
                new(0, 1, 0, 0, 0, 1, 64, 64, 64, 255),
            ],
            Indices = [0, 1, 2],
            MaterialName = "Model2_Mesh1",
        };
        var model2Mesh2 = new ReferenceMeshData
        {
            Vertices =
            [
                new(0, 0, 0, 0, 0, 1, 192, 192, 192, 255),
                new(1, 0, 0, 0, 0, 1, 192, 192, 192, 255),
                new(0, 1, 0, 0, 0, 1, 192, 192, 192, 255),
            ],
            Indices = [0, 1, 2],
            MaterialName = "Model2_Mesh2",
        };
        var model2 = new ReferenceModelData
        {
            FilePath = "/fake/model2.fbx",
            Format = "fbx",
            Meshes = [model2Mesh1, model2Mesh2],
            IsVisible = true,
            Scale = 1f,
        };

        var refModels = new ReferenceModelState();
        refModels.Add(model1);
        refModels.Add(model2);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        // Expect 4 materials, one per mesh
        Assert.Equal(4, snapshot.Materials.Count);
        Assert.Equal(2, snapshot.ReferenceNodes.Count);

        // Model1 has 2 meshes → primitives[0].MaterialIndex = 0, primitives[1].MaterialIndex = 1
        Assert.Equal(2, snapshot.ReferenceNodes[0].Primitives.Count);
        Assert.Equal(0, snapshot.ReferenceNodes[0].Primitives[0].MaterialIndex);
        Assert.Equal(1, snapshot.ReferenceNodes[0].Primitives[1].MaterialIndex);

        // Model2 has 2 meshes → primitives[0].MaterialIndex = 2, primitives[1].MaterialIndex = 3
        Assert.Equal(2, snapshot.ReferenceNodes[1].Primitives.Count);
        Assert.Equal(2, snapshot.ReferenceNodes[1].Primitives[0].MaterialIndex);
        Assert.Equal(3, snapshot.ReferenceNodes[1].Primitives[1].MaterialIndex);

        // Verify material names match what we assigned
        Assert.Equal("Model1_Mesh1", snapshot.Materials[0].Name);
        Assert.Equal("Model1_Mesh2", snapshot.Materials[1].Name);
        Assert.Equal("Model2_Mesh1", snapshot.Materials[2].Name);
        Assert.Equal("Model2_Mesh2", snapshot.Materials[3].Name);
    }

    // ── UV detection ──

    [Fact]
    public void BuildUvSets_FirstVertexZeroUv_ScansAllVerticesAndFindsNonZero()
    {
        // First vertex has UV = (0, 0), but subsequent vertices have valid UVs.
        // Old code only checked verts[0] and would incorrectly return no UVs.
        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 255, 255, 255, 255, 0, 0),
            new ReferenceVertex(1, 0, 0, 0, 0, 1, 255, 255, 255, 255, 0.5f, 0),
            new ReferenceVertex(0, 1, 0, 0, 0, 1, 255, 255, 255, 255, 1f, 1f),
        };

        var model = CreateReferenceModel(vertices: vertices);
        var refModels = new ReferenceModelState();
        refModels.Add(model);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        // Should have one reference node with one primitive with UVs
        var node = Assert.Single(snapshot.ReferenceNodes);
        var prim = Assert.Single(node.Primitives);
        Assert.NotEmpty(prim.UvSets);
        Assert.Equal(3 * 2, prim.UvSets[0].Uvs.Length); // 3 vertices × 2 components
    }

    [Fact]
    public void BuildUvSets_AllUvsAreZero_ReturnsEmpty()
    {
        // All vertices have UV = (0, 0) — genuinely UV-less mesh.
        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 255, 255, 255, 0, 0),
            new ReferenceVertex(1, 0, 0, 0, 0, 1, 255, 255, 255, 0, 0),
            new ReferenceVertex(0, 1, 0, 0, 0, 1, 255, 255, 255, 0, 0),
        };

        var model = CreateReferenceModel(vertices: vertices);
        var refModels = new ReferenceModelState();
        refModels.Add(model);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        var node = Assert.Single(snapshot.ReferenceNodes);
        var prim = Assert.Single(node.Primitives);
        Assert.Empty(prim.UvSets);
    }

    // ── Transformed reference bounds ──

    [Fact]
    public void BuildSnapshot_TransformedReference_WorldBoundsRespectTransform()
    {
        // A 1×1×1 axis-aligned triangle at origin, scaled to 2x→ should produce bounds ±2 in world
        var model = CreateReferenceModel(
            posX: 5, posY: 10, posZ: -3,
            scale: 2f,
            vertices:
            [
                new(0, 0, 0, 0, 0, 1, 255, 255, 255, 255),
                new(1, 0, 0, 0, 0, 1, 255, 255, 255, 255),
                new(0, 1, 0, 0, 0, 1, 255, 255, 255, 255),
            ]);

        var refModels = new ReferenceModelState();
        refModels.Add(model);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        var node = Assert.Single(snapshot.ReferenceNodes);
        Assert.NotNull(node.BoundsLocal);
        Assert.NotNull(node.BoundsWorld);

        // Local bounds: min (0,0,0), max (1,1,0) for the triangle
        Assert.Equal(0, node.BoundsLocal.MinX);
        Assert.Equal(0, node.BoundsLocal.MinY);
        Assert.Equal(0, node.BoundsLocal.MinZ);
        Assert.Equal(1, node.BoundsLocal.MaxX);
        Assert.Equal(1, node.BoundsLocal.MaxY);
        Assert.Equal(0, node.BoundsLocal.MaxZ);

        // World bounds: Scale 2x → extents 0..2, then translate by (5, 10, -3)
        Assert.Equal(5, node.BoundsWorld.MinX);
        Assert.Equal(10, node.BoundsWorld.MinY);
        Assert.Equal(-3, node.BoundsWorld.MinZ);
        Assert.Equal(7, node.BoundsWorld.MaxX);
        Assert.Equal(12, node.BoundsWorld.MaxY);
        Assert.Equal(-3, node.BoundsWorld.MaxZ);

        // Snapshot-level ReferenceBounds (world-space aggregate) should match
        Assert.NotNull(snapshot.ReferenceBounds);
        Assert.Equal(5, snapshot.ReferenceBounds.MinX);
        Assert.Equal(10, snapshot.ReferenceBounds.MinY);
        Assert.Equal(-3, snapshot.ReferenceBounds.MinZ);
    }

    // ── Hidden models ──

    [Fact]
    public void BuildSnapshot_HiddenModel_ExcludedFromAggregateBounds()
    {
        // Two models: one visible, one hidden.
        // Hidden model should not contribute to snapshot.ReferenceBounds or CombinedBounds.
        var visibleModel = CreateReferenceModel(
            filePath: "/fake/visible.obj",
            posX: 0, posY: 0, posZ: 0,
            scale: 1f,
            visible: true);

        var hiddenModel = CreateReferenceModel(
            filePath: "/fake/hidden.obj",
            posX: 100, posY: 100, posZ: 100,
            scale: 1f,
            visible: false);

        var refModels = new ReferenceModelState();
        refModels.Add(visibleModel);
        refModels.Add(hiddenModel);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        Assert.Equal(2, snapshot.ReferenceNodes.Count);
        Assert.True(snapshot.ReferenceNodes[0].Visible);
        Assert.False(snapshot.ReferenceNodes[1].Visible);

        // Both have BoundsWorld computed
        Assert.NotNull(snapshot.ReferenceNodes[0].BoundsWorld);
        Assert.NotNull(snapshot.ReferenceNodes[1].BoundsWorld);

        // ReferenceBounds should only include visible node
        Assert.NotNull(snapshot.ReferenceBounds);
        Assert.True(snapshot.ReferenceBounds.MaxX < 10, "Hidden model's large offset should not be in bounds");

        // Combined bounds should also exclude hidden model
        Assert.NotNull(snapshot.CombinedBounds);
        Assert.True(snapshot.CombinedBounds.MaxX < 10);
    }

    [Fact]
    public void BuildSnapshot_AllModelsHidden_NoReferenceBounds()
    {
        var hiddenModel = CreateReferenceModel(
            filePath: "/fake/hidden.obj",
            visible: false);

        var refModels = new ReferenceModelState();
        refModels.Add(hiddenModel);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        Assert.Null(snapshot.ReferenceBounds);
        Assert.Null(snapshot.CombinedBounds);
    }

    // ── Texture handle behavior ──

    [Fact]
    public void BuildSnapshot_TextureUrisAreHostSafe()
    {
        // Texture URIs should use the `texture://{hostId}/{texId}` scheme,
        // not raw filesystem paths.
        var texturePath = "/tmp/test_texture.png";
        try
        {
            // Create a temporary texture file so the service picks it up
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]); // minimal PNG header

            var mesh = new ReferenceMeshData
            {
                Vertices =
                [
                    new(0, 0, 0, 0, 0, 1, 255, 255, 255, 255, 1, 0),
                    new(1, 0, 0, 0, 0, 1, 255, 255, 255, 255, 0, 1),
                    new(0, 1, 0, 0, 0, 1, 255, 255, 255, 255, 0, 0),
                ],
                Indices = [0, 1, 2],
                DiffuseTexturePath = texturePath,
                DiffuseTextureSource = "assimp",
                MaterialName = "textured_mat",
            };

            var model = new ReferenceModelData
            {
                FilePath = "/fake/textured.obj",
                Format = "obj",
                Meshes = [mesh],
                IsVisible = true,
                Scale = 1f,
            };

            var refModels = new ReferenceModelState();
            refModels.Add(model);

            var workspace = CreateWorkspace(referenceModels: refModels);
            var meshService = new MeshSnapshotService(new GreedyMesher());
            var paletteService = new PaletteSnapshotService();
            var service = new RenderSceneSnapshotService(meshService, paletteService);

            var snapshot = service.BuildSnapshot(workspace, hostId: "mcp");

            // Texture URI must use transport-handle, not raw filesystem path
            var texture = Assert.Single(snapshot.Textures);
            Assert.StartsWith("texture://mcp/", texture.Uri);
            Assert.DoesNotContain("/tmp/", texture.Uri);
            Assert.DoesNotContain("test_texture.png", texture.Uri);

            // Material should reference that texture
            var material = Assert.Single(snapshot.Materials);
            Assert.NotNull(material.BaseColorTexture);
            Assert.Equal(texture.Id, material.BaseColorTexture.TextureId);
        }
        finally
        {
            if (File.Exists(texturePath))
                File.Delete(texturePath);
        }
    }

    // ── Material diagnostics ──

    [Fact]
    public void BuildSnapshot_MaterialWithoutNormalEmissive_HasDiagnostics()
    {
        var model = CreateReferenceModel(materialName: "no_textures_mat");
        var refModels = new ReferenceModelState();
        refModels.Add(model);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        var material = Assert.Single(snapshot.Materials);
        Assert.NotEmpty(material.Diagnostics);

        Assert.Contains(material.Diagnostics, d => d.Category == "material.normal");
        Assert.Contains(material.Diagnostics, d => d.Category == "material.emissive");
        Assert.Contains(material.Diagnostics, d => d.Category == "material.alpha");
        Assert.Contains(material.Diagnostics, d => d.Category == "material.double_sided");
    }

    // ── Vertex alpha → alpha mode inference ──

    [Fact]
    public void BuildSnapshot_VertexAlphaBelow255_InfersBlendAlphaMode()
    {
        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 255, 255, 255, 128), // A=128
            new ReferenceVertex(1, 0, 0, 0, 0, 1, 255, 255, 255, 128),
            new ReferenceVertex(0, 1, 0, 0, 0, 1, 255, 255, 255, 128),
        };

        var model = CreateReferenceModel(vertices: vertices, materialName: "alpha_mat");
        var refModels = new ReferenceModelState();
        refModels.Add(model);

        var workspace = CreateWorkspace(referenceModels: refModels);
        var meshService = new MeshSnapshotService(new GreedyMesher());
        var paletteService = new PaletteSnapshotService();
        var service = new RenderSceneSnapshotService(meshService, paletteService);

        var snapshot = service.BuildSnapshot(workspace, hostId: "test");

        var material = Assert.Single(snapshot.Materials);
        Assert.Equal("blend", material.AlphaMode);
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
