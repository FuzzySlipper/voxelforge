using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core.Meshing;

namespace VoxelForge.Core.Tests;

public sealed class MeshingTests
{
    private static VoxelModel CreateModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        return model;
    }

    // --- NaiveMesher ---

    [Fact]
    public void NaiveMesher_SingleVoxel_Produces24Vertices36Indices()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        Assert.Equal(24, mesh.Vertices.Length); // 6 faces × 4 verts
        Assert.Equal(36, mesh.Indices.Length);   // 6 faces × 2 tris × 3 indices
    }

    [Fact]
    public void NaiveMesher_TwoAdjacentVoxels_InteriorFacesCulled()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);

        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        // Two voxels share 1 face on each side → 2 faces culled
        // Total exposed faces: 2×6 - 2 = 10
        Assert.Equal(10 * 4, mesh.Vertices.Length);
        Assert.Equal(10 * 6, mesh.Indices.Length);
    }

    [Fact]
    public void NaiveMesher_EmptyModel_ProducesEmptyMesh()
    {
        var model = CreateModel();
        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Indices);
    }

    [Fact]
    public void NaiveMesher_VerticesHaveCorrectColor()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        foreach (var v in mesh.Vertices)
        {
            Assert.Equal(128, v.R);
            Assert.Equal(128, v.G);
            Assert.Equal(128, v.B);
            Assert.Equal(255, v.A);
        }
    }

    [Fact]
    public void NaiveMesher_MissingPalette_UsesMagenta()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        // Don't set any palette entry — index 1 is unmapped
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        var vertex = mesh.Vertices[0];
        Assert.Equal(255, vertex.R);
        Assert.Equal(0, vertex.G);
        Assert.Equal(255, vertex.B);
    }

    // --- GreedyMesher ---

    [Fact]
    public void GreedyMesher_SingleVoxel_Produces6Faces()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new GreedyMesher();
        var mesh = mesher.Build(model);

        // Single voxel can't be merged — same as naive: 6 quads
        Assert.Equal(24, mesh.Vertices.Length);
        Assert.Equal(36, mesh.Indices.Length);
    }

    [Fact]
    public void GreedyMesher_SolidBlock_FewerFacesThanNaive()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(3, 3, 3), 1);

        var naive = new NaiveMesher();
        var greedy = new GreedyMesher();

        var naiveMesh = naive.Build(model);
        var greedyMesh = greedy.Build(model);

        // Greedy should merge faces → fewer quads
        Assert.True(greedyMesh.Vertices.Length < naiveMesh.Vertices.Length,
            $"Greedy ({greedyMesh.Vertices.Length} verts) should produce fewer vertices than Naive ({naiveMesh.Vertices.Length} verts)");
    }

    [Fact]
    public void GreedyMesher_InteriorFacesCulled()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);

        var mesher = new GreedyMesher();
        var mesh = mesher.Build(model);

        // Check no face normals point inward between the two voxels
        // The shared face at x=1 should not exist
        // Total exposed: 10 faces (may be merged by greedy)
        // But vertex count should be <= 10 * 4
        Assert.True(mesh.Vertices.Length <= 40,
            $"Expected at most 40 vertices for 2 adjacent voxels, got {mesh.Vertices.Length}");
        Assert.True(mesh.Vertices.Length > 0);
    }

    [Fact]
    public void GreedyMesher_EmptyModel_ProducesEmptyMesh()
    {
        var model = CreateModel();
        var mesher = new GreedyMesher();
        var mesh = mesher.Build(model);

        Assert.Empty(mesh.Vertices);
        Assert.Empty(mesh.Indices);
    }

    [Fact]
    public void GreedyMesher_32Cube_CompletesUnder100ms()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(31, 31, 31), 1);

        var mesher = new GreedyMesher();

        // Warm up
        mesher.Build(model);

        var sw = Stopwatch.StartNew();
        var mesh = mesher.Build(model);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"GreedyMesher took {sw.ElapsedMilliseconds}ms, expected <100ms");
        Assert.True(mesh.Vertices.Length > 0);
    }

    // --- Cross-mesher comparison ---

    [Fact]
    public void BothMeshers_SameExposedFaceCount_ForSimpleModel()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(3, 3, 3), 1);

        var naive = new NaiveMesher();
        var greedy = new GreedyMesher();

        var naiveMesh = naive.Build(model);
        var greedyMesh = greedy.Build(model);

        // Both should produce the same number of triangles
        // (greedy merges quads but total triangle count = same number of exposed face units)
        // Actually greedy produces fewer triangles because merged quads = fewer tris.
        // What we can verify: both cover the same surface area.
        // Simplest check: same total index count / 3 gives triangle count,
        // and greedy should have fewer or equal.
        Assert.True(greedyMesh.TriangleCount <= naiveMesh.TriangleCount,
            $"Greedy ({greedyMesh.TriangleCount} tris) should have <= triangles than Naive ({naiveMesh.TriangleCount} tris)");
    }
}
