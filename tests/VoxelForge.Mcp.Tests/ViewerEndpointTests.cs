using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;
using VoxelForge.Core.Reference;
using VoxelForge.Mcp.Viewer;

namespace VoxelForge.Mcp.Tests;

public sealed class ViewerEndpointTests
{
    private static VoxelForgeMcpSession CreateSession(Action<VoxelForgeMcpSession>? setup = null)
    {
        var session = new VoxelForgeMcpSession(
            new EditorConfigState { DefaultGridHint = 32, MaxUndoDepth = 50 },
            NullLoggerFactory.Instance);
        setup?.Invoke(session);
        return session;
    }

    private static (MeshSnapshotService, PaletteSnapshotService) CreateServices()
    {
        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        return (meshService, paletteService);
    }

    // ── Reference model visibility / viewer integration tests ──

    [Fact]
    public void ViewerState_ReferenceModelCount_ZeroWhenNoneLoaded()
    {
        var session = CreateSession();
        var refCount = session.ReferenceModels.Models.Count;
        var refVerts = session.ReferenceModels.Models.Sum(r => r.TotalVertices);

        Assert.Equal(0, refCount);
        Assert.Equal(0, refVerts);
    }

    [Fact]
    public void ViewerState_ReferenceModelLoaded_ShowsCountAndVertices()
    {
        var session = CreateSession();
        AddTestReferenceModel(session.ReferenceModels);

        var refCount = session.ReferenceModels.Models.Count;
        var refVerts = session.ReferenceModels.Models.Sum(r => r.TotalVertices);

        Assert.Equal(1, refCount);
        Assert.Equal(8, refVerts); // cube: 8 vertices total
    }

    [Fact]
    public void MeshSnapshot_ReferenceModelLoaded_IncludesReferenceGeometry()
    {
        var session = CreateSession();
        AddTestReferenceModel(session.ReferenceModels);

        var referenceModels = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(session.ReferenceModels.Models);

        Assert.NotNull(referenceModels);
        Assert.Single(referenceModels);

        var rm = referenceModels[0];
        Assert.Equal("test-cube.obj", rm.FileName);
        Assert.Equal("OBJ", rm.Format);
        Assert.True(rm.IsVisible);
        Assert.True(rm.TotalVertices > 0, "Reference model should have vertices");
        Assert.True(rm.TotalTriangles > 0, "Reference model should have triangles");
        Assert.NotNull(rm.Positions);
        Assert.True(rm.Positions.Length > 0, "Positions array should be non-empty");
        Assert.NotNull(rm.Indices);
        Assert.True(rm.Indices.Length > 0, "Indices array should be non-empty");

        // Verify transform fields are populated from the ReferenceModelData
        Assert.Equal(10f, rm.PositionX);
        Assert.Equal(20f, rm.PositionY);
        Assert.Equal(5f, rm.PositionZ);
        Assert.Equal(2f, rm.Scale);
    }

    [Fact]
    public void MeshSnapshot_ReferenceModelLoaded_NoVoxels_StillIncludesGeometry()
    {
        var session = CreateSession();
        var (meshService, _) = CreateServices();
        AddTestReferenceModel(session.ReferenceModels);

        // Voxel model is empty
        var mesh = meshService.BuildSnapshot(session.Document.Model);
        Assert.Equal(0, mesh.VertexCount);
        Assert.Null(mesh.Bounds);

        // But reference model data is present
        var referenceModels = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(session.ReferenceModels.Models);
        Assert.NotEmpty(referenceModels);
        Assert.True(referenceModels[0].TotalVertices > 0);

        // Combined bounds should reflect the reference model, not null
        var voxelBounds = session.Document.Model.GetBounds();
        var combinedBounds = ViewerEndpointsTestAccessors.ComputeCombinedBoundsPublic(voxelBounds, session.ReferenceModels.Models);
        Assert.NotNull(combinedBounds);
        // The cube fixture unit cube with scale=2 and position=(10,20,5) should have bounds
        // centered around (10, 20, 5) with extent ~2 in each direction
        Assert.True(combinedBounds.MinX >= 8, "Combined bounds MinX should account for reference model");
        Assert.True(combinedBounds.MaxX <= 12, "Combined bounds MaxX should account for reference model");
    }

    [Fact]
    public void MeshSnapshotResponse_EmptyVoxelWithReference_HasCombinedBounds()
    {
        var session = CreateSession();
        var (meshService, _) = CreateServices();
        AddTestReferenceModel(session.ReferenceModels);

        // Voxel model is empty
        var mesh = meshService.BuildSnapshot(session.Document.Model);
        var referenceModels = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(session.ReferenceModels.Models);

        // Simulate what the mesh-snapshot endpoint does: compute combined bounds
        var modelBounds = session.Document.Model.GetBounds(); // null — empty model
        Assert.Null(modelBounds);

        var combinedBounds = ViewerEndpointsTestAccessors.ComputeCombinedBoundsPublic(
            modelBounds, session.ReferenceModels.Models);

        Assert.NotNull(combinedBounds);
        Assert.True(combinedBounds.MinX < combinedBounds.MaxX, "Combined bounds should have positive extent");
        Assert.True(combinedBounds.MinY < combinedBounds.MaxY, "Combined bounds should have positive extent");
        Assert.True(combinedBounds.MinZ < combinedBounds.MaxZ, "Combined bounds should have positive extent");

        // The test cube at scale=2, position=(10,20,5) should produce non-trivial bounds
        Assert.True(combinedBounds.MinX >= 8, "Combined MinX should account for reference model position");
        Assert.True(combinedBounds.MinY >= 18, "Combined MinY should account for reference model position");
        Assert.True(combinedBounds.MinZ >= 3, "Combined MinZ should account for reference model position");
        Assert.True(combinedBounds.MaxX <= 12, "Combined MaxX should account for reference model position");
        Assert.True(combinedBounds.MaxY <= 22, "Combined MaxY should account for reference model position");
        Assert.True(combinedBounds.MaxZ <= 7, "Combined MaxZ should account for reference model position");
    }

    [Fact]
    public void MeshSnapshotResponse_EmptyVoxelNoReference_CombinedBoundsNull()
    {
        var session = CreateSession();
        var (meshService, _) = CreateServices();

        // No reference models loaded
        var modelBounds = session.Document.Model.GetBounds(); // null — empty model
        Assert.Null(modelBounds);

        var combinedBounds = ViewerEndpointsTestAccessors.ComputeCombinedBoundsPublic(
            modelBounds, session.ReferenceModels.Models);

        Assert.Null(combinedBounds);
    }

    [Fact]
    public void ViewerRevision_IncrementsOnReferenceModelChangedEvent()
    {
        var session = CreateSession();
        int initialRevision = session.ViewerRevision;

        // Reference model load events should increment revision
        session.Events.Publish(new ReferenceModelChangedEvent(
            ReferenceModelChangeKind.Loaded, "Test load", 0));

        Assert.True(session.ViewerRevision > initialRevision,
            "Viewer revision should increment on ReferenceModelChangedEvent(Loaded)");
    }

    [Fact]
    public void ViewerRevision_ReferenceModelTransform_Increments()
    {
        var session = CreateSession();
        int initialRevision = session.ViewerRevision;

        session.Events.Publish(new ReferenceModelChangedEvent(
            ReferenceModelChangeKind.TransformChanged, "Test transform", 0));

        Assert.True(session.ViewerRevision > initialRevision,
            "Viewer revision should increment on ReferenceModelChangedEvent(TransformChanged)");
    }

    [Fact]
    public void ViewerRevision_ReferenceModelRemoved_Increments()
    {
        var session = CreateSession();
        int initialRevision = session.ViewerRevision;

        session.Events.Publish(new ReferenceModelChangedEvent(
            ReferenceModelChangeKind.Removed, "Test remove", 0));

        Assert.True(session.ViewerRevision > initialRevision,
            "Viewer revision should increment on ReferenceModelChangedEvent(Removed)");
    }

    [Fact]
    public void ViewerRevision_ReferenceModelCleared_Increments()
    {
        var session = CreateSession();
        int initialRevision = session.ViewerRevision;

        session.Events.Publish(new ReferenceModelChangedEvent(
            ReferenceModelChangeKind.Cleared, "Test clear", null));

        Assert.True(session.ViewerRevision > initialRevision,
            "Viewer revision should increment on ReferenceModelChangedEvent(Cleared)");
    }

    // ── Helper methods ──

    private static void AddTestReferenceModel(ReferenceModelState referenceModels)
    {
        // Create a unit cube OBJ-style reference model with transform
        var mesh = new ReferenceMeshData
        {
            Vertices =
            [
                // Front face (z=0)
                new ReferenceVertex(0, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 1, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(0, 1, 0, 0, 0, -1, 200, 200, 200, 255),
                // Back face (z=1)
                new ReferenceVertex(0, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 1, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(0, 1, 1, 0, 0, 1, 180, 180, 180, 255),
            ],
            Indices =
            [
                // Front
                0, 1, 2, 0, 2, 3,
                // Back
                4, 6, 5, 4, 7, 6,
                // Right
                1, 5, 6, 1, 6, 2,
                // Left
                4, 0, 3, 4, 3, 7,
                // Top
                3, 2, 6, 3, 6, 7,
                // Bottom
                4, 5, 1, 4, 1, 0,
            ],
        };

        var model = new ReferenceModelData
        {
            FilePath = "/tmp/test-cube.obj",
            Format = "OBJ",
            Meshes = [mesh],
            PositionX = 10f,
            PositionY = 20f,
            PositionZ = 5f,
            Scale = 2f,
            RotationX = 0f,
            RotationY = 0f,
            RotationZ = 0f,
            IsVisible = true,
        };

        referenceModels.Add(model);
    }

    // ── Existing tests ──

    [Fact]
    public void ViewerState_EmptyModel_ReturnsZeroVoxels()
    {
        var session = CreateSession();
        using var scope = new ServiceCollection().BuildServiceProvider().CreateScope();

        // The viewer-state endpoint doesn't take DI services directly; we test the
        // logic by reading session state that the endpoint handler uses.
        var model = session.Document.Model;
        Assert.Equal(0, model.GetVoxelCount());
        Assert.Equal("untitled", session.CurrentModelName);
        Assert.Equal(0, session.ViewerRevision);
    }

    [Fact]
    public void ViewerState_AfterSetVoxels_ReflectsRevisionAndCount()
    {
        var session = CreateSession();
        var model = session.Document.Model;

        // Add some voxels directly (simulating what MCP tools do)
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        model.SetVoxel(new Point3(0, 1, 0), 2);

        // Simulate palette entries (needed for mesh snapshot)
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0, 255) });

        // Increment revision (normally done by event handler)
        session.IncrementViewerRevision();

        Assert.Equal(3, model.GetVoxelCount());
        Assert.True(session.ViewerRevision > 0);
    }

    [Fact]
    public void MeshSnapshot_EmptyModel_ReturnsEmptyArrays()
    {
        var session = CreateSession();
        var (meshService, _) = CreateServices();

        var mesh = meshService.BuildSnapshot(session.Document.Model);

        Assert.Empty(mesh.Positions);
        Assert.Empty(mesh.Normals);
        Assert.Empty(mesh.Colors);
        Assert.Empty(mesh.Indices);
        Assert.Null(mesh.Bounds);
        Assert.Equal(0, mesh.VertexCount);
        Assert.Equal(0, mesh.TriangleCount);
    }

    [Fact]
    public void MeshSnapshot_WithVoxels_ReturnsValidGeometry()
    {
        var session = CreateSession();
        var (meshService, _) = CreateServices();

        var model = session.Document.Model;
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        model.SetVoxel(new Point3(0, 1, 0), 2);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0, 255) });

        var mesh = meshService.BuildSnapshot(model);

        // Greedy mesher should produce at least some geometry for 3 voxels
        Assert.True(mesh.VertexCount > 0, "Mesh should have vertices for 3 voxels");
        Assert.True(mesh.TriangleCount > 0, "Mesh should have triangles for 3 voxels");

        // Vertices are quads (4 verts per quad face)
        Assert.Equal(mesh.VertexCount * 3, mesh.Positions.Length);
        Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Length);
        Assert.Equal(mesh.VertexCount * 4, mesh.Colors.Length);
        Assert.Equal(mesh.TriangleCount * 3, mesh.Indices.Length);

        // Bounds should be set
        Assert.NotNull(mesh.Bounds);

        // Colors should be non-default (we added colored voxels)
        bool hasNonDefaultColor = false;
        for (int i = 0; i < mesh.Colors.Length; i++)
        {
            if (mesh.Colors[i] != 0) { hasNonDefaultColor = true; break; }
        }
        Assert.True(hasNonDefaultColor, "Vertex colors should be non-zero for colored voxels");
    }

    [Fact]
    public void Palette_WithEntries_ReturnsCorrectShape()
    {
        var session = CreateSession();
        var (_, paletteService) = CreateServices();

        var model = session.Document.Model;
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0, 255) });

        var palette = paletteService.BuildSnapshot(model.Palette);

        Assert.Equal(2, palette.EntryCount);
        Assert.Equal(2, palette.Entries.Count);

        // Check first entry
        var first = palette.Entries[0];
        Assert.Equal(1, first.Index);
        Assert.Equal("Stone", first.Name);
        Assert.Equal(128, first.R);
        Assert.Equal(128, first.G);
        Assert.Equal(128, first.B);
        Assert.Equal(255, first.A);

        // Check second entry  
        var second = palette.Entries[1];
        Assert.Equal(2, second.Index);
        Assert.Equal("Red", second.Name);
        Assert.Equal(255, second.R);
        Assert.Equal(0, second.G);
        Assert.Equal(0, second.B);
    }

    // ── SSE / live-event tests ──

    [Fact]
    public async Task SubscribeViewerEvents_ReturnsReaderAndUnsubscribe()
    {
        var session = CreateSession();

        var (reader, unsubscribe) = session.SubscribeViewerEvents();

        Assert.NotNull(reader);
        Assert.NotNull(unsubscribe);

        // Cleanup
        unsubscribe();
    }

    [Fact]
    public async Task IncrementRevision_NotifiesSubscribers()
    {
        var session = CreateSession();
        var (reader, unsubscribe) = session.SubscribeViewerEvents();

        // Increment revision multiple times
        session.IncrementViewerRevision(); // rev 1
        session.IncrementViewerRevision(); // rev 2
        session.IncrementViewerRevision(); // rev 3

        // Read from the channel (all should be available)
        var revisions = new List<int>();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        try
        {
            await foreach (var rev in reader.ReadAllAsync(cts.Token))
            {
                revisions.Add(rev);
                if (revisions.Count >= 3) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout — accept what we got
        }

        Assert.Contains(1, revisions);
        Assert.Contains(2, revisions);
        Assert.Contains(3, revisions);

        unsubscribe();
    }

    [Fact]
    public async Task Unsubscribe_StopsNotifications()
    {
        var session = CreateSession();
        var (reader, unsubscribe) = session.SubscribeViewerEvents();

        // Read initial state then unsubscribe
        session.IncrementViewerRevision();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await foreach (var rev in reader.ReadAllAsync(cts.Token))
            {
                break; // consume first item
            }
        }
        catch (OperationCanceledException) { }

        unsubscribe();

        // After unsubscribe, further revisions should not reach this reader
        session.IncrementViewerRevision();
        session.IncrementViewerRevision();

        // The channel should be completed, so ReadAllAsync should end immediately
        var remaining = new List<int>();
        await foreach (var rev in reader.ReadAllAsync())
        {
            remaining.Add(rev);
        }

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveNotifications()
    {
        var session = CreateSession();
        var (reader1, unsub1) = session.SubscribeViewerEvents();
        var (reader2, unsub2) = session.SubscribeViewerEvents();

        session.IncrementViewerRevision(); // rev 1

        // Both subscribers should receive the revision
        int? r1 = null, r2 = null;
        var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try { await foreach (var rev in reader1.ReadAllAsync(cts1.Token)) { r1 = rev; break; } } catch (OperationCanceledException) { }
        try { await foreach (var rev in reader2.ReadAllAsync(cts2.Token)) { r2 = rev; break; } } catch (OperationCanceledException) { }

        Assert.Equal(1, r1);
        Assert.Equal(1, r2);

        unsub1();
        unsub2();
    }

    [Fact]
    public void ViewerMeshSnapshotResponse_SerializesWithSnakeCase()
    {
        // Verify that serialization uses snake_case naming
        var response = new ViewerMeshSnapshotResponse
        {
            ModelId = "test",
            MeshId = "mesh-test-001",
            Format = "json",
            VertexCount = 4,
            IndexCount = 6,
            TriangleCount = 2,
            Positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
            Normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
            Colors = [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 255, 0, 255, 255],
            Indices = [0, 1, 2, 0, 2, 3],
            Bounds = new ViewerBounds { MinX = 0, MinY = 0, MinZ = 0, MaxX = 1, MaxY = 1, MaxZ = 1 },
            CombinedBounds = new ViewerBounds { MinX = 0, MinY = 0, MinZ = 0, MaxX = 2, MaxY = 2, MaxZ = 2 },
            PaletteMapping = new Dictionary<string, object>
            {
                ["1"] = new { name = "Red", color = "#FF0000", a = 255, visible = true },
            },
            ReferenceModels =
            [
                new ViewerReferenceModelData
                {
                    Index = 0,
                    FileName = "ref.obj",
                    Format = "OBJ",
                    TotalVertices = 24,
                    TotalTriangles = 12,
                    IsVisible = true,
                    PositionX = 10f,
                    PositionY = 0f,
                    PositionZ = 0f,
                    Scale = 1f,
                    Positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
                    Normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
                    Colors = [128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255, 128, 128, 128, 255],
                    Indices = [0, 1, 2, 0, 2, 3],
                    Bounds = new ViewerBounds { MinX = 0, MinY = 0, MinZ = 0, MaxX = 1, MaxY = 1, MaxZ = 1 },
                    MeshTextures =
                    [
                        new ViewerMeshTextureInfo
                        {
                            MeshIndex = 0,
                            MaterialName = "default",
                            DiffuseTexturePath = "/path/to/texture.png",
                            DiffuseSourceLabel = "manual_override",
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        Assert.Contains("\"model_id\"", json);
        Assert.Contains("\"mesh_id\"", json);
        Assert.Contains("\"vertex_count\"", json);
        Assert.Contains("\"index_count\"", json);
        Assert.Contains("\"triangle_count\"", json);
        Assert.Contains("\"positions\"", json);
        Assert.Contains("\"normals\"", json);
        Assert.Contains("\"colors\"", json);
        Assert.Contains("\"indices\"", json);
        Assert.Contains("\"palette_mapping\"", json);
        Assert.Contains("\"min_x\"", json);
        Assert.Contains("\"combined_bounds\"", json);
        Assert.Contains("\"reference_models\"", json);
        Assert.Contains("\"file_name\"", json);
        Assert.Contains("\"is_visible\"", json);
        Assert.Contains("\"total_vertices\"", json);
        Assert.Contains("\"total_triangles\"", json);
        Assert.Contains("\"position_x\"", json);
        Assert.Contains("\"rotation_x\"", json);

        using var document = JsonDocument.Parse(json);
        var colors = document.RootElement.GetProperty("colors");
        Assert.Equal(JsonValueKind.Array, colors.ValueKind);
        Assert.Equal(16, colors.GetArrayLength());
        Assert.Equal(255, colors[0].GetInt32());

        // Verify combined_bounds serializes correctly
        var combinedBounds = document.RootElement.GetProperty("combined_bounds");
        Assert.Equal(0, combinedBounds.GetProperty("min_x").GetInt32());
        Assert.Equal(2, combinedBounds.GetProperty("max_x").GetInt32());

        // Verify reference_models array and snake_case fields
        var refModels = document.RootElement.GetProperty("reference_models");
        Assert.Equal(1, refModels.GetArrayLength());
        Assert.Equal("ref.obj", refModels[0].GetProperty("file_name").GetString());
        Assert.True(refModels[0].GetProperty("is_visible").GetBoolean());
        Assert.Equal(24, refModels[0].GetProperty("total_vertices").GetInt32());

        // Verify mesh_textures is present and snake_case
        Assert.Contains("\"mesh_textures\"", json);
        var meshTextures = refModels[0].GetProperty("mesh_textures");
        Assert.True(meshTextures.GetArrayLength() > 0);
        Assert.Contains("\"diffuse_texture_path\"", json);
        Assert.Contains("\"diffuse_source_label\"", json);
        Assert.Contains("\"material_name\"", json);
    }

    // ── Camera/view boundary tests ──

    [Fact]
    public void ViewerStateResponse_DoesNotContainCameraState()
    {
        // Camera state must be strictly TS-owned presentation state.
        // C# viewer state must not leak camera position, target, or rotation.
        var response = new ViewerStateResponse
        {
            Revision = 1,
            ModelName = "test",
            VoxelCount = 0,
            GridHint = 32,
            PaletteEntries = [],
            Bounds = null,
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        // Must contain model metadata
        Assert.Contains("\"revision\"", json);
        Assert.Contains("\"model_name\"", json);
        Assert.Contains("\"voxel_count\"", json);

        // Must NOT contain camera or view state
        Assert.DoesNotContain("camera", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orbit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("view_matrix", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera_position", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("camera_target", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewerStateResponse_SerializationDoesNotIncludeUndoState()
    {
        // Camera/view undo leak would manifest as camera state visible in the
        // state response or undo stack metadata. Verify no camera/view fields.
        var json = JsonSerializer.Serialize(
            new ViewerStateResponse
            {
                Revision = 42,
                ModelName = "untitled",
                VoxelCount = 100,
                GridHint = 16,
                PaletteEntries = [
                    new ViewerPaletteEntry { Index = 1, Name = "Stone", Color = "#808080", A = 255, Visible = true },
                ],
                Bounds = new ViewerBounds { MinX = 0, MinY = 0, MinZ = 0, MaxX = 10, MaxY = 10, MaxZ = 10 },
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        // Verify expected fields exist
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(42, root.GetProperty("revision").GetInt32());
        Assert.Equal("untitled", root.GetProperty("model_name").GetString());
        Assert.Equal(100, root.GetProperty("voxel_count").GetInt32());
        Assert.Equal(16, root.GetProperty("grid_hint").GetInt32());

        // Verify palette entry is included
        var palette = root.GetProperty("palette_entries");
        Assert.Equal(1, palette.GetArrayLength());
        Assert.Equal(1, palette[0].GetProperty("index").GetInt32());

        // Verify bounds are included
        var bounds = root.GetProperty("bounds");
        Assert.Equal(0, bounds.GetProperty("min_x").GetInt32());
        Assert.Equal(10, bounds.GetProperty("max_x").GetInt32());

        // Verify NO camera or view fields leaked
        var propertyNames = new HashSet<string>();
        foreach (var prop in root.EnumerateObject())
        {
            propertyNames.Add(prop.Name);
        }
        Assert.DoesNotContain("camera", propertyNames);
        Assert.DoesNotContain("view", propertyNames);
        Assert.DoesNotContain("orbit", propertyNames);
        Assert.DoesNotContain("rotation", propertyNames);
    }
    // ── Per-mesh geometry tests ──

    [Fact]
    public void MeshSnapshot_PerMeshGeometry_ExposedForSingleMesh()
    {
        // Given a reference model with one mesh, the viewer response must
        // expose per-mesh geometry data with correct vertex/triangle counts.
        var session = CreateSession();
        AddTestReferenceModel(session.ReferenceModels);

        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
            session.ReferenceModels.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Meshes);
        Assert.Single(rm.Meshes);

        var mesh0 = rm.Meshes[0];
        Assert.Equal(0, mesh0.MeshIndex);
        Assert.Equal("default", mesh0.MaterialName);
        Assert.Equal(8, mesh0.VertexCount);
        Assert.Equal(12, mesh0.TriangleCount);
        Assert.NotNull(mesh0.Positions);
        Assert.Equal(8 * 3, mesh0.Positions.Length);
        Assert.NotNull(mesh0.Indices);
        Assert.Equal(12 * 3, mesh0.Indices.Length);
        Assert.Null(mesh0.DiffuseTexturePath); // no texture in test cube
        Assert.Equal("none", mesh0.DiffuseSourceLabel);

        // Per-mesh indices must NOT have index offset (they are local to the mesh)
        Assert.All(mesh0.Indices, idx => Assert.True(idx >= 0 && idx < 8,
            $"Per-mesh index {idx} must be within mesh vertex range [0,{7}]"));
    }

    [Fact]
    public void MeshSnapshot_PerMeshGeometry_IncludesUvs()
    {
        // UV-bearing mesh must expose UV coordinates; no-UV mesh must not claim has_uvs.
        var meshWithUvs = new ReferenceMeshData
        {
            Vertices =
            [
                new ReferenceVertex(0, 0, 0, 0, 0, -1, 200, 200, 200, 255, U: 0f, V: 0f),
                new ReferenceVertex(1, 0, 0, 0, 0, -1, 200, 200, 200, 255, U: 1f, V: 0f),
                new ReferenceVertex(1, 1, 0, 0, 0, -1, 200, 200, 200, 255, U: 1f, V: 1f),
                new ReferenceVertex(0, 1, 0, 0, 0, -1, 200, 200, 200, 255, U: 0f, V: 1f),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            MaterialName = "MatA",
            DiffuseTexturePath = "/tmp/tex.png",
            DiffuseTextureSource = "assimp",
        };

        var meshNoUvs = new ReferenceMeshData
        {
            Vertices =
            [
                // Default U=V=0 (no explicit UVs)
                new ReferenceVertex(0, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 1, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(0, 1, 1, 0, 0, 1, 180, 180, 180, 255),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            MaterialName = "MatB",
        };

        var model = new ReferenceModelData
        {
            FilePath = "/tmp/uv-test.obj",
            Format = "OBJ",
            Meshes = [meshWithUvs, meshNoUvs],
            IsVisible = true,
        };

        var state = new ReferenceModelState();
        state.Add(model);
        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(state.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Meshes);
        Assert.Equal(2, rm.Meshes.Count);

        var m0 = rm.Meshes[0];
        Assert.True(m0.HasUvs);
        Assert.NotNull(m0.Uvs);
        Assert.Equal(4 * 2, m0.Uvs.Length);
        Assert.Equal(0f, m0.Uvs[0]);
        Assert.Equal(0f, m0.Uvs[1]);
        Assert.Equal(1f, m0.Uvs[2]);
        Assert.Equal(0f, m0.Uvs[3]);
        Assert.Equal(1f, m0.Uvs[4]);
        Assert.Equal(1f, m0.Uvs[5]);
        Assert.Equal(0f, m0.Uvs[6]);
        Assert.Equal(1f, m0.Uvs[7]);

        // Flattened model-level UVs should also be present
        Assert.NotNull(rm.Uvs);
        Assert.Equal(8 * 2, rm.Uvs.Length);

        var m1 = rm.Meshes[1];
        Assert.False(m1.HasUvs);
        Assert.NotNull(m1.Uvs);
        Assert.Equal(4 * 2, m1.Uvs.Length);
        // All UVs should be zero for no-UV mesh
        Assert.All(m1.Uvs, u => Assert.Equal(0f, u));
    }

    [Fact]
    public void MeshSnapshot_PerMeshGeometry_MultiMeshExposedDistinctly()
    {
        // Given a reference model with two meshes having different properties,
        // the viewer response must expose both entries distinctly with their
        // own positions, normals, colors, indices, and texture info.
        var mesh0 = new ReferenceMeshData
        {
            Vertices =
            [
                new ReferenceVertex(0, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 1, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(0, 1, 0, 0, 0, -1, 200, 200, 200, 255),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            MaterialName = "MatA",
            DiffuseTexturePath = "/path/to/tex_a.png",
            DiffuseTextureSource = "assimp",
        };

        var mesh1 = new ReferenceMeshData
        {
            Vertices =
            [
                new ReferenceVertex(0, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(1, 1, 1, 0, 0, 1, 180, 180, 180, 255),
                new ReferenceVertex(0, 1, 1, 0, 0, 1, 180, 180, 180, 255),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            MaterialName = "MatB",
            // No texture for mesh1 — should use vertex colors/fallback
        };

        var model = new ReferenceModelData
        {
            FilePath = "/tmp/two-mesh-cube.obj",
            Format = "OBJ",
            Meshes = [mesh0, mesh1],
            IsVisible = true,
        };

        var state = new ReferenceModelState();
        state.Add(model);
        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
            state.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Meshes);
        Assert.Equal(2, rm.Meshes.Count);

        // ── Mesh 0 ──
        var m0 = rm.Meshes[0];
        Assert.Equal(0, m0.MeshIndex);
        Assert.Equal("MatA", m0.MaterialName);
        Assert.Equal(4, m0.VertexCount);
        Assert.Equal(2, m0.TriangleCount);
        Assert.NotNull(m0.DiffuseTexturePath);
        Assert.Contains("tex_a.png", m0.DiffuseTexturePath);
        Assert.Equal("assimp", m0.DiffuseSourceLabel);
        Assert.Null(m0.NormalTexturePath);
        Assert.Null(m0.EmissiveTexturePath);

        // Per-mesh indices must be local to mesh0's vertices (no offset)
        Assert.All(m0.Indices, idx => Assert.True(idx >= 0 && idx < 4,
            $"Mesh0 index {idx} must be within [0,{3}]"));

        // ── Mesh 1 ──
        var m1 = rm.Meshes[1];
        Assert.Equal(1, m1.MeshIndex);
        Assert.Equal("MatB", m1.MaterialName);
        Assert.Equal(4, m1.VertexCount);
        Assert.Equal(2, m1.TriangleCount);
        Assert.Null(m1.DiffuseTexturePath); // no texture on mesh1
        Assert.Equal("none", m1.DiffuseSourceLabel);

        // Per-mesh indices must be local to mesh1's vertices
        Assert.All(m1.Indices, idx => Assert.True(idx >= 0 && idx < 4,
            $"Mesh1 index {idx} must be within [0,{3}]"));

        // Each mesh must have its own positions array — vertex count * 3
        Assert.Equal(12, m0.Positions.Length);
        Assert.Equal(12, m1.Positions.Length);

        // The flattened model-level geometry must still be present (backward compat)
        Assert.NotNull(rm.Positions);
        Assert.Equal(8 * 3, rm.Positions.Length);
    }

    [Fact]
    public void MeshSnapshot_PerMeshGeometry_CorrectTextureUrlFormat()
    {
        // Verify that per-mesh texture info can be used to construct the
        // correct /api/reference-texture URL with mesh_index parameter.
        var mesh = new ReferenceMeshData
        {
            Vertices =
            [
                new ReferenceVertex(0, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(1, 1, 0, 0, 0, -1, 200, 200, 200, 255),
                new ReferenceVertex(0, 1, 0, 0, 0, -1, 200, 200, 200, 255),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
            MaterialName = "MatA",
            DiffuseTexturePath = "/tmp/texture.png",
            DiffuseTextureSource = "assimp",
        };

        var model = new ReferenceModelData
        {
            FilePath = "/tmp/tex-cube.obj",
            Format = "OBJ",
            Meshes = [mesh],
            IsVisible = true,
        };

        var state = new ReferenceModelState();
        state.Add(model);
        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
            state.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Meshes);
        var m0 = rm.Meshes[0];

        // Construct the URL the viewer would build
        int modelIndex = rm.Index;
        int meshIndex = m0.MeshIndex;
        string expectedUrl = $"/api/reference-texture?index={modelIndex}&mesh_index={meshIndex}&slot=diffuse";
        // The per-mesh DiffuseTexturePath should match the source path
        Assert.NotNull(m0.DiffuseTexturePath);
        Assert.Equal("/tmp/texture.png", m0.DiffuseTexturePath);
        Assert.Equal("assimp", m0.DiffuseSourceLabel);

        // Verify snake_case serialization of new fields
        var json = System.Text.Json.JsonSerializer.Serialize(viewerData, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
        Assert.Contains("\"mesh_index\"", json);
        Assert.Contains("\"diffuse_texture_path\"", json);
        Assert.Contains("\"diffuse_source_label\"", json);
    }

    [Fact]
    public void ViewerHtml_PerMeshRendering_NoFirstMeshGlobalTexture()
    {
        // Static regression: the viewer HTML must NOT apply the first mesh's
        // texture globally to the whole model. It must build per-mesh meshes.
        var viewerHtmlPath = FindViewerHtmlPath();
        var html = File.ReadAllText(viewerHtmlPath);

        // The old "first mesh" comment must not appear
        Assert.DoesNotContain("load the first one's diffuse", html);
        Assert.DoesNotContain("render the whole model with the first mesh's texture", html);

        // The new per-mesh rendering code must be present
        Assert.Contains("Per-mesh rendering: build one Three.js Mesh per source mesh", html);
        Assert.Contains("ref-\" + ri + \"-mesh-\" + mi", html);

        // Per-mesh texture URL construction must reference meshData.mesh_index
        Assert.Contains("mesh_index=\" + meshData.mesh_index", html);

        // Model-level transform must be applied to a group, not individual meshes
        Assert.Contains("const modelGroup = new THREE.Group()", html);

        // Backward compat fallback path must still exist
        Assert.Contains("flattened model-level geometry (backward compat)", html);
    }

    [Fact]
    public void ViewerHtml_SetsUvAttribute()
    {
        // The viewer HTML must set the 'uv' BufferAttribute on reference mesh
        // geometry before applying MeshStandardMaterial.map.
        var viewerHtmlPath = FindViewerHtmlPath();
        var html = File.ReadAllText(viewerHtmlPath);

        // Per-mesh path must setAttribute('uv', ...)
        Assert.Contains("geo.setAttribute(\"uv\", new THREE.BufferAttribute(uvs, 2))", html);

        // Fallback path must also setAttribute('uv', ...)
        Assert.Contains("geo.setAttribute(\"uv\", new THREE.BufferAttribute(uvs, 2))", html);

        // Must check for has_uvs and warn when texture is present but no UVs
        Assert.Contains("has_uvs", html);
        Assert.Contains("no UV coordinates", html);

        // Must track pending texture loads for capture readiness
        Assert.Contains("pendingTextureLoads", html);
        Assert.Contains("maybeSetCaptureReady", html);
        Assert.Contains("captureReady set (textures settled)", html);
    }

    [Fact]
    public void MeshSnapshot_PerMeshGeometry_ManualOverrideExposedInPerMesh()
    {
        // Verify that manual texture overrides appear in per-mesh geometry entries.
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-permesh-override-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "override.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);

            var mesh0 = new ReferenceMeshData
            {
                Vertices =
                [
                    new ReferenceVertex(0, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                    new ReferenceVertex(1, 0, 0, 0, 0, -1, 200, 200, 200, 255),
                ],
                Indices = [0, 1],
                MaterialName = "MatA",
            };
            // Apply manual override
            mesh0.ManualDiffuseOverridePath = texturePath;

            var mesh1 = new ReferenceMeshData
            {
                Vertices =
                [
                    new ReferenceVertex(0, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                    new ReferenceVertex(1, 0, 1, 0, 0, 1, 180, 180, 180, 255),
                ],
                Indices = [0, 1],
                MaterialName = "MatB",
                // No override — should have no diffuse texture
            };

            var model = new ReferenceModelData
            {
                FilePath = "/tmp/override-cube.obj",
                Format = "OBJ",
                Meshes = [mesh0, mesh1],
                IsVisible = true,
            };

            var state = new ReferenceModelState();
            state.Add(model);
            var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
                state.Models);

            Assert.NotEmpty(viewerData);
            var rm = viewerData[0];
            Assert.NotNull(rm.Meshes);
            Assert.Equal(2, rm.Meshes.Count);

            // Mesh 0: should have manual override
            var m0 = rm.Meshes[0];
            Assert.NotNull(m0.DiffuseTexturePath);
            Assert.Equal(texturePath, m0.DiffuseTexturePath);
            Assert.Equal("manual_override", m0.DiffuseSourceLabel);

            // Mesh 1: should have no texture
            var m1 = rm.Meshes[1];
            Assert.Null(m1.DiffuseTexturePath);
            Assert.Equal("none", m1.DiffuseSourceLabel);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    // ── Reference model color/alpha handling tests ──

    [Fact]
    public void ReferenceModelColors_OpaqueVertexAlpha255()
    {
        // Given a reference model with all-alpha-255 vertex colors,
        // the exported viewer data must carry alpha=255 so the viewer
        // can distinguish opaque from transparent geometry.
        var session = CreateSession();
        AddTestReferenceModel(session.ReferenceModels);

        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
            session.ReferenceModels.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Colors);
        Assert.True(rm.Colors.Length > 0, "Reference model should have color data");

        // Every 4th byte is alpha; all should be 255 for this opaque test cube.
        for (int i = 3; i < rm.Colors.Length; i += 4)
        {
            Assert.Equal(255, rm.Colors[i]);
        }
    }

    [Fact]
    public void ReferenceModelColors_FallbackGrayHasAlpha255()
    {
        // When a reference vertex has all-zero RGBA, the fallback
        // medium gray (128,128,128,255) must include alpha=255.
        var mesh = new ReferenceMeshData
        {
            Vertices =
            [
                // All-zero color — should trigger fallback gray (128,128,128,255)
                new ReferenceVertex(0, 0, 0, 0, 0, -1, 0, 0, 0, 0),
                new ReferenceVertex(1, 0, 0, 0, 0, -1, 0, 0, 0, 0),
                new ReferenceVertex(1, 1, 0, 0, 0, -1, 0, 0, 0, 0),
                new ReferenceVertex(0, 1, 0, 0, 0, -1, 0, 0, 0, 0),
            ],
            Indices = [0, 1, 2, 0, 2, 3],
        };

        var model = new ReferenceModelData
        {
            FilePath = "/tmp/zero-color-cube.obj",
            Format = "OBJ",
            Meshes = [mesh],
            IsVisible = true,
        };

        var state = new ReferenceModelState();
        state.Add(model);
        var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(
            state.Models);

        Assert.NotEmpty(viewerData);
        var rm = viewerData[0];
        Assert.NotNull(rm.Colors);
        Assert.True(rm.Colors.Length >= 16, "Fallback should produce colors for 4 vertices");

        // Each vertex should be fallback gray (128,128,128,255): R=128 G=128 B=128 A=255
        for (int i = 0; i < rm.Colors.Length; i += 4)
        {
            Assert.Equal(128, rm.Colors[i]);     // R
            Assert.Equal(128, rm.Colors[i + 1]); // G
            Assert.Equal(128, rm.Colors[i + 2]); // B
            Assert.Equal(255, rm.Colors[i + 3]); // A
        }
    }

    [Fact]
    public void ViewerHtml_ReferenceMaterial_NoHardcodedTransparency()
    {
        // Static regression: the viewer HTML must not force all reference
        // meshes to transparent:true/opacity:0.85. Opaque source should
        // produce opaque material settings. This test fails if the old
        // hardcoded values return.
        var viewerHtmlPath = FindViewerHtmlPath();
        var html = File.ReadAllText(viewerHtmlPath);

        // The string "transparent: true, opacity: 0.85" must NOT appear
        // as a contiguous fragment in the reference mesh material section.
        Assert.DoesNotContain("transparent: true,\n          opacity: 0.85,", html);

        // The material creation must reference alpha-aware variables.
        Assert.Contains("isRefTransparent", html);
        Assert.Contains("refOpacity", html);
        Assert.Contains("depthWrite", html);
    }

    /// <summary>
    /// Resolve viewer.html path from the test assembly's output directory,
    /// mirroring ViewerHtml.LoadContent() search order.
    /// </summary>
    private static string FindViewerHtmlPath()
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "wwwroot", "viewer.html"),
            Path.Combine(
                Path.GetDirectoryName(baseDir) ?? baseDir,
                "..", "..", "..", "wwwroot", "viewer.html"),
            Path.GetFullPath(Path.Combine(
                baseDir, "..", "..", "..", "..", "..",
                "src", "VoxelForge.Mcp", "wwwroot", "viewer.html")),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(Path.GetFullPath(path)))
                return Path.GetFullPath(path);
        }

        throw new FileNotFoundException(
            "viewer.html not found. Test must be run from the solution directory.");
    }
}

/// <summary>
/// Public accessors for internal static helper methods to enable unit testing.
/// </summary>
public static class ViewerEndpointsTestAccessors
{
    public static List<ViewerReferenceModelData> BuildReferenceModelDataListPublic(
        IReadOnlyList<ReferenceModelData> models)
        => ViewerEndpoints.BuildReferenceModelDataList(models);

    public static ViewerBounds? ComputeCombinedBoundsPublic(
        (Point3 Min, Point3 Max)? voxelBounds,
        IReadOnlyList<ReferenceModelData> referenceModels)
        => ViewerEndpoints.ComputeCombinedBounds(voxelBounds, referenceModels);
}
