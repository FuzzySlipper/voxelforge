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
            PaletteMapping = new Dictionary<string, object>
            {
                ["1"] = new { name = "Red", color = "#FF0000", a = 255, visible = true },
            },
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

        using var document = JsonDocument.Parse(json);
        var colors = document.RootElement.GetProperty("colors");
        Assert.Equal(JsonValueKind.Array, colors.ValueKind);
        Assert.Equal(16, colors.GetArrayLength());
        Assert.Equal(255, colors[0].GetInt32());
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
