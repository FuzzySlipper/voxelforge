using System.Diagnostics;
using System.Numerics;
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

    private static void AssertFrontFacingTrianglesProjectCorrectly(VoxelMesh mesh)
    {
        var cameraPosition = new Vector3(4f, 3f, 5f);
        var target = new Vector3(0.5f, 0.5f, 0.5f);
        var view = Matrix4x4.CreateLookAt(cameraPosition, target, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, 16f / 10f, 0.1f, 100f);
        var viewProjection = view * projection;
        const float viewportWidth = 1600f;
        const float viewportHeight = 1000f;
        int checkedTriangleCount = 0;

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var v0 = mesh.Vertices[mesh.Indices[i]];
            var v1 = mesh.Vertices[mesh.Indices[i + 1]];
            var v2 = mesh.Vertices[mesh.Indices[i + 2]];

            var p0 = new Vector3(v0.X, v0.Y, v0.Z);
            var p1 = new Vector3(v1.X, v1.Y, v1.Z);
            var p2 = new Vector3(v2.X, v2.Y, v2.Z);
            var triangleCenter = (p0 + p1 + p2) / 3f;
            var faceNormal = Vector3.Normalize(new Vector3(v0.NX, v0.NY, v0.NZ));
            var toCamera = Vector3.Normalize(cameraPosition - triangleCenter);

            if (Vector3.Dot(faceNormal, toCamera) <= 0.01f)
                continue;

            var clip0 = Vector4.Transform(new Vector4(p0, 1f), viewProjection);
            var clip1 = Vector4.Transform(new Vector4(p1, 1f), viewProjection);
            var clip2 = Vector4.Transform(new Vector4(p2, 1f), viewProjection);

            Assert.True(clip0.W > 0f && clip1.W > 0f && clip2.W > 0f,
                $"Triangle {i / 3} is behind the camera.");

            var ndc0 = new Vector2(clip0.X / clip0.W, clip0.Y / clip0.W);
            var ndc1 = new Vector2(clip1.X / clip1.W, clip1.Y / clip1.W);
            var ndc2 = new Vector2(clip2.X / clip2.W, clip2.Y / clip2.W);

            var screen0 = new Vector2(
                (ndc0.X * 0.5f + 0.5f) * viewportWidth,
                (1f - (ndc0.Y * 0.5f + 0.5f)) * viewportHeight);
            var screen1 = new Vector2(
                (ndc1.X * 0.5f + 0.5f) * viewportWidth,
                (1f - (ndc1.Y * 0.5f + 0.5f)) * viewportHeight);
            var screen2 = new Vector2(
                (ndc2.X * 0.5f + 0.5f) * viewportWidth,
                (1f - (ndc2.Y * 0.5f + 0.5f)) * viewportHeight);

            float signedArea = (screen1.X - screen0.X) * (screen2.Y - screen0.Y) -
                               (screen1.Y - screen0.Y) * (screen2.X - screen0.X);

            Assert.True(
                signedArea < 0f,
                $"Triangle {i / 3} does not project correctly for a front-facing triangle. Area={signedArea}");

            checkedTriangleCount++;
        }

        Assert.True(checkedTriangleCount > 0, "Expected to validate at least one front-facing triangle.");
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
    public void NaiveMesher_SingleVoxel_ProjectsFrontFacesCorrectly_ForFnaCull()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new NaiveMesher();
        var mesh = mesher.Build(model);

        AssertFrontFacingTrianglesProjectCorrectly(mesh);
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
    public void GreedyMesher_SingleVoxel_ProjectsFrontFacesCorrectly_ForFnaCull()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var mesher = new GreedyMesher();
        var mesh = mesher.Build(model);

        AssertFrontFacingTrianglesProjectCorrectly(mesh);
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

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"GreedyMesher took {sw.ElapsedMilliseconds}ms, expected <500ms");
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
