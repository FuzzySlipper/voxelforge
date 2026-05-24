using System.Numerics;
using System.Text.Json;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

/// <summary>
/// Tests for <see cref="ReferenceMeshProbeHelper"/> geometry operations and
/// MCP tool adapters for raycast/silhouette/histogram probes.
/// Uses synthetic tiny triangle/cube meshes (no external GLB files required).
/// </summary>
public sealed class ReferenceMeshProbeTests
{
    // --------------- Helper: build synthetic meshes ---------------

    /// <summary>
    /// Create a unit cube [0,1] with 12 triangles (2 per face), 8 vertices.
    /// </summary>
    private static ReferenceModelData MakeUnitCubeModel(
        float scale = 1f,
        float posX = 0, float posY = 0, float posZ = 0,
        float rotX = 0, float rotY = 0, float rotZ = 0)
    {
        // 8 vertices of a unit cube
        var verts = new ReferenceVertex[]
        {
            // floor (y=0)
            new(0, 0, 0, 0, -1, 0, 200, 200, 200, 255),
            new(1, 0, 0, 0, -1, 0, 200, 200, 200, 255),
            new(1, 0, 1, 0, -1, 0, 200, 200, 200, 255),
            new(0, 0, 1, 0, -1, 0, 200, 200, 200, 255),
            // top (y=1)
            new(0, 1, 0, 0, 1, 0, 200, 200, 200, 255),
            new(1, 1, 0, 0, 1, 0, 200, 200, 200, 255),
            new(1, 1, 1, 0, 1, 0, 200, 200, 200, 255),
            new(0, 1, 1, 0, 1, 0, 200, 200, 200, 255),
        };

        // 12 triangles (36 indices), 2 per face
        var indices = new int[]
        {
            // bottom (y=0, normal 0,-1,0)
            0, 1, 2, 0, 2, 3,
            // top (y=1, normal 0,1,0)
            4, 6, 5, 4, 7, 6,
            // front (z=0, normal 0,0,-1)
            0, 4, 5, 0, 5, 1,
            // back (z=1, normal 0,0,1)
            3, 2, 6, 3, 6, 7,
            // left (x=0, normal -1,0,0)
            0, 3, 7, 0, 7, 4,
            // right (x=1, normal 1,0,0)
            1, 5, 6, 1, 6, 2,
        };

        var mesh = new ReferenceMeshData
        {
            Vertices = verts,
            Indices = indices,
            MaterialName = "default",
        };

        return new ReferenceModelData
        {
            FilePath = "/synthetic/cube.obj",
            Format = "OBJ",
            Meshes = [mesh],
            PositionX = posX,
            PositionY = posY,
            PositionZ = posZ,
            RotationX = rotX,
            RotationY = rotY,
            RotationZ = rotZ,
            Scale = scale,
        };
    }

    /// <summary>
    /// Create a single triangle at z=0 for deterministic ray hit testing.
    /// Triangle: (0,0,0), (1,0,0), (0,1,0)
    /// </summary>
    private static ReferenceModelData MakeSingleTriangleModel()
    {
        var verts = new ReferenceVertex[]
        {
            new(0, 0, 0, 0, 0, -1, 255, 0, 0, 255),
            new(1, 0, 0, 0, 0, -1, 255, 0, 0, 255),
            new(0, 1, 0, 0, 0, -1, 255, 0, 0, 255),
        };

        var mesh = new ReferenceMeshData
        {
            Vertices = verts,
            Indices = [0, 1, 2],
            MaterialName = "red_triangle",
        };

        return new ReferenceModelData
        {
            FilePath = "/synthetic/triangle.obj",
            Format = "OBJ",
            Meshes = [mesh],
            Scale = 1f,
        };
    }

    // --------------- Raycast tests ---------------

    [Fact]
    public void Raycast_HitsTriangleAtOrigin()
    {
        var model = MakeSingleTriangleModel();
        // Ray from (0.25, 0.25, 10) toward -Z should hit the triangle at z=0
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(0.25f, 0.25f, 10f),
            new Vector3(0, 0, -1),
            maxHits: 5);

        Assert.Equal(1, result.HitCount);
        Assert.NotNull(result.Hits);
        Assert.Single(result.Hits);
        Assert.Equal(0, result.Hits[0].MeshIndex);
        Assert.Equal(0, result.Hits[0].TriangleIndex);
        Assert.Equal(10f, result.Hits[0].Distance, 4);
        Assert.Equal(0f, result.Hits[0].Point.Z, 4);
    }

    [Fact]
    public void Raycast_MissesTriangle()
    {
        var model = MakeSingleTriangleModel();
        // Ray from (-10, 0, 0) along X should miss the triangle at (0,0,0)-(1,0,0)-(0,1,0)
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(-10f, 0.5f, 0f),
            new Vector3(1, 0, 0),
            maxHits: 5);

        Assert.Equal(0, result.HitCount);
        Assert.Null(result.Hits);
    }

    [Fact]
    public void Raycast_ZeroDirectionFails()
    {
        var model = MakeSingleTriangleModel();
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(0, 0, 0),
            new Vector3(0, 0, 0),
            maxHits: 5);

        // Zero direction should produce zero hits
        Assert.Equal(0, result.HitCount);
        Assert.Null(result.Hits);
    }

    [Fact]
    public void Raycast_HitsUnitCubeFrontFace()
    {
        var model = MakeUnitCubeModel();
        // Ray from (0.5, 0.5, 10) toward -Z: cube occupies z∈[0,1], ray hits z=1 first (back face)
        // at distance 9, then z=0 (front face) at distance 10.
        // (Moller-Trumbore doesn't cull back-faces, so 4 triangles hit: 2 front + 2 back)
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(0.5f, 0.5f, 10f),
            new Vector3(0, 0, -1),
            maxHits: 5);

        Assert.Equal(4, result.HitCount);
        Assert.NotNull(result.Hits);
        // Nearest hit is the back face (z=1) at distance 9
        Assert.Equal(9f, result.Hits[0].Distance, 4);
        Assert.Equal(1f, result.Hits[0].Point.Z, 4);
    }

    [Fact]
    public void Raycast_TransformedCube_MultipleHits()
    {
        var model = MakeUnitCubeModel(scale: 2f, posX: 5, posY: 0, posZ: 0);
        // Ray from (0, 0.5, 0.5) along +X should hit the scaled/translated cube
        // Cube world AABB: [5, 7] x [0, 2] x [0, 2]
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(0f, 1f, 1f),
            new Vector3(1, 0, 0),
            maxHits: 10);

        Assert.True(result.HitCount > 0, "Should hit transformed cube");
        Assert.NotNull(result.Hits);
        Assert.Equal(5f, result.Hits[0].Distance, 4); // 5 units to x=5
        Assert.Equal(5f, result.Hits[0].Point.X, 4);
    }

    [Fact]
    public void Raycast_CapsMaxHits()
    {
        var model = MakeUnitCubeModel();
        // Shoot many rays to get all 12 triangles hit? Actually, from outside
        // the cube, a single ray can hit at most 1 triangle (the front face).
        // Let's just verify the cap works: maxHits=1 should return at most 1
        var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
            model,
            new Vector3(0.5f, 0.5f, 10f),
            new Vector3(0, 0, -1),
            maxHits: 1);

        Assert.Equal(1, result.HitCount);
        Assert.NotNull(result.Hits);
        Assert.Single(result.Hits);
    }

    // --------------- Axis histogram tests ---------------

    [Fact]
    public void AxisHistogram_UnitCubeY_UniformDistribution()
    {
        var model = MakeUnitCubeModel();
        var result = ReferenceMeshProbeHelper.ComputeAxisHistogram(model, 'y', bins: 8);

        Assert.Equal("y", result.Axis);
        Assert.Equal(8, result.BinCount);
        Assert.Equal(8, result.TotalSamples); // 8 vertices, each sampled once
        // Unit cube has 8 vertices, with 2 unique Y values (0 and 1)
        Assert.True(result.MinValue >= 0);
        Assert.True(result.MaxValue <= 1);
        Assert.NotNull(result.Mean);
        Assert.NotNull(result.Median);
    }

    [Fact]
    public void AxisHistogram_SingleTriangle_AllAtZ0()
    {
        var model = MakeSingleTriangleModel();
        var result = ReferenceMeshProbeHelper.ComputeAxisHistogram(model, 'z', bins: 4);

        Assert.Equal("z", result.Axis);
        Assert.Equal(3, result.TotalSamples);
        Assert.Equal(0f, result.MinValue);
        Assert.Equal(0f, result.MaxValue);
    }

    [Fact]
    public void AxisHistogram_Transformed_ReflectsScale()
    {
        var model = MakeUnitCubeModel(scale: 10f);
        var result = ReferenceMeshProbeHelper.ComputeAxisHistogram(model, 'y', bins: 10, transformed: true);

        // After scale 10, vertices at y=0 and y=10
        Assert.True(result.MaxValue >= 9f, $"Expected maxValue >= 9, got {result.MaxValue}");
        Assert.True(result.MinValue >= -0.1f, $"Expected minValue >= -0.1, got {result.MinValue}");
    }

    // --------------- Silhouette probe tests ---------------

    [Fact]
    public void SampleViews_UnitCube_OccupancyDetected()
    {
        var model = MakeUnitCubeModel();
        var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(
            model,
            views: ["front", "right", "top"],
            resolution: 16);

        Assert.Equal(3, result.ViewCount);
        Assert.True(result.TotalOccupiedSamples > 0);

        // Front view should see the square (z=0 face is [0,1]x[0,1])
        var frontView = result.Views[0];
        Assert.Equal("front", frontView.ViewName);
        Assert.True(frontView.OccupiedSamples > 0, "Front view should have occupied samples");
        Assert.True(frontView.OccupancyDensity > 0);

        // With ray-casting, the nearest hit depth is the front face (z=0).
        // For front view with look=(0,0,-1): depth = dot((x,y,0),(0,0,-1)) = 0.
        // All rays that hit the cube first intersect at z=0 (front face).
        Assert.Equal(0, frontView.DepthMin, 4);

        // Depth values should be finite and consistent for a flat front face
        Assert.True(float.IsFinite(frontView.DepthMax));
        Assert.Equal(frontView.DepthMin, frontView.DepthMax, 4);
        Assert.NotNull(frontView.MedianDepth);
        Assert.True(float.IsFinite(frontView.MedianDepth.Value));

        // Run-length rows should be present
        Assert.NotNull(frontView.RunLengthRows);
        Assert.Equal(16, frontView.RunLengthRows.Count);
    }

    [Fact]
    public void SampleViews_UnitCube_FilledSilhouette()
    {
        // A unit cube sampled from front at resolution 8 should produce
        // a filled or near-filled square silhouette (occupied_samples >= 60 of 64).
        var model = MakeUnitCubeModel();
        var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(
            model,
            views: ["front"],
            resolution: 8);

        Assert.Single(result.Views);
        var frontView = result.Views[0];
        Assert.Equal("front", frontView.ViewName);

        // 60+ of 64 cells occupied = filled square, not just corner dots
        Assert.True(frontView.OccupiedSamples >= 60,
            $"Expected >= 60 occupied samples for front view of cube at res 8, got {frontView.OccupiedSamples}");
        Assert.True(frontView.OccupancyDensity >= 60f / 64f);
    }

    [Fact]
    public void SampleViews_SingleTriangle_PartialOccupancy()
    {
        // A single triangle sampled from front at resolution 8 should occupy
        // more than its 3 vertices but not all 64 cells.
        var model = MakeSingleTriangleModel();
        var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(
            model,
            views: ["front"],
            resolution: 8);

        Assert.Single(result.Views);
        var frontView = result.Views[0];

        // Should occupy more than just the 3 vertex dots
        Assert.True(frontView.OccupiedSamples > 3,
            $"Expected > 3 occupied samples for triangle at res 8, got {frontView.OccupiedSamples}");

        // But less than full grid
        Assert.True(frontView.OccupiedSamples < 52,
            $"Expected < 52 occupied samples for single triangle at res 8, got {frontView.OccupiedSamples}. " +
            $"A single triangle covers roughly half the square.");

        // Depth should be finite
        Assert.True(float.IsFinite(frontView.DepthMin));
        Assert.True(float.IsFinite(frontView.DepthMax));
        Assert.NotNull(frontView.MedianDepth);
        Assert.True(float.IsFinite(frontView.MedianDepth.Value));
    }

    [Fact]
    public void SampleViews_EmptyModel_NoOccupancy()
    {
        // Model with no vertices
        var mesh = new ReferenceMeshData
        {
            Vertices = [],
            Indices = [],
            MaterialName = "empty",
        };
        var model = new ReferenceModelData
        {
            FilePath = "/synthetic/empty.obj",
            Format = "OBJ",
            Meshes = [mesh],
            Scale = 1f,
        };

        var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(
            model,
            views: ["front"],
            resolution: 8);

        Assert.Equal(1, result.ViewCount);
        Assert.Equal(0, result.TotalOccupiedSamples);
    }

    [Fact]
    public void SampleViews_CapsResolution()
    {
        var model = MakeUnitCubeModel();
        var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(
            model,
            views: ["front"],
            resolution: 999); // should cap to MaxOrthoResolution

        Assert.Single(result.Views);
        Assert.Equal(ReferenceMeshProbeHelper.MaxOrthoResolution, result.Views[0].Resolution);
    }

    // --------------- MCP tool adapter tests ---------------

    [Fact]
    public void RaycastMcpTool_BadIndex_Fails()
    {
        var session = CreateSession();
        var tool = new RaycastReferenceModelMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "origin": {"x":0,"y":0,"z":0}, "direction": {"x":0,"y":0,"z":1} }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model at index", result.Message);
    }

    [Fact]
    public void RaycastMcpTool_ZeroDirection_Fails()
    {
        var model = MakeUnitCubeModel();
        var session = CreateSession();
        session.ReferenceModels.Add(model);
        var tool = new RaycastReferenceModelMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "origin": {"x":0,"y":0,"z":0}, "direction": {"x":0,"y":0,"z":0} }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("non-zero", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RaycastMcpTool_HitsTriangle()
    {
        var model = MakeSingleTriangleModel();
        var session = CreateSession();
        session.ReferenceModels.Add(model);
        var tool = new RaycastReferenceModelMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "origin": {"x":0.25,"y":0.25,"z":10}, "direction": {"x":0,"y":0,"z":-1}, "max_hits": 3 }
            """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(1, doc.RootElement.GetProperty("hit_count").GetInt32());
        var hits = doc.RootElement.GetProperty("hits");
        Assert.Single(hits.EnumerateArray());
        Assert.Equal(0, hits[0].GetProperty("mesh_index").GetInt32());
        Assert.Equal(0, hits[0].GetProperty("triangle_index").GetInt32());
        Assert.Equal(10f, hits[0].GetProperty("distance").GetSingle(), 4);
    }

    [Fact]
    public void SampleViewsMcpTool_BadIndex_Fails()
    {
        var session = CreateSession();
        var tool = new SampleReferenceModelViewsMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0 }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model at index", result.Message);
    }

    [Fact]
    public void SampleViewsMcpTool_ReturnsOccupancy()
    {
        var model = MakeUnitCubeModel();
        var session = CreateSession();
        session.ReferenceModels.Add(model);
        var tool = new SampleReferenceModelViewsMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "views": ["front"], "resolution": 8 }
            """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(1, doc.RootElement.GetProperty("view_count").GetInt32());
        var views = doc.RootElement.GetProperty("views");
        Assert.Single(views.EnumerateArray());
        Assert.Equal("front", views[0].GetProperty("view_name").GetString());
    }

    [Fact]
    public void AxisHistogramMcpTool_BadIndex_Fails()
    {
        var session = CreateSession();
        var tool = new ReferenceModelAxisHistogramMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0 }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model at index", result.Message);
    }

    [Fact]
    public void AxisHistogramMcpTool_ReturnsHistogram()
    {
        var model = MakeUnitCubeModel();
        var session = CreateSession();
        session.ReferenceModels.Add(model);
        var tool = new ReferenceModelAxisHistogramMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "axis": "y", "bins": 4 }
            """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal("y", doc.RootElement.GetProperty("axis").GetString());
        Assert.Equal(4, doc.RootElement.GetProperty("bin_count").GetInt32());
        Assert.Equal(8, doc.RootElement.GetProperty("total_samples").GetInt32());
    }

    [Fact]
    public void AxisHistogramMcpTool_InvalidAxis_Fails()
    {
        var model = MakeUnitCubeModel();
        var session = CreateSession();
        session.ReferenceModels.Add(model);
        var tool = new ReferenceModelAxisHistogramMcpTool(session);

        var result = tool.Invoke(JsonArguments("""
            { "index": 0, "axis": "w" }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Axis must be", result.Message);
    }

    // --------------- Helpers ---------------

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new App.EditorConfigState(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
    }

    private static JsonElement JsonArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
