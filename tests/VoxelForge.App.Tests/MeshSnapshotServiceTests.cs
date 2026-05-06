using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Tests;

public sealed class MeshSnapshotServiceTests
{
    private static VoxelModel CreateModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        model.Palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0) });
        return model;
    }

    [Fact]
    public void BuildSnapshot_SingleVoxel_ProducesNonEmptySnapshot()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        Assert.True(snapshot.VertexCount > 0, "Should have vertices for a single voxel");
        Assert.True(snapshot.TriangleCount > 0, "Should have triangles for a single voxel");
        Assert.NotNull(snapshot.PaletteIndices);
        Assert.Equal(snapshot.VertexCount, snapshot.PaletteIndices!.Length);
    }

    [Fact]
    public void BuildSnapshot_EmptyModel_ProducesEmptySnapshot()
    {
        var model = CreateModel();

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        Assert.Equal(0, snapshot.VertexCount);
        Assert.Equal(0, snapshot.TriangleCount);
        Assert.Null(snapshot.Bounds);
    }

    [Fact]
    public void EmptySnapshot_ReturnsZeroedSnapshot()
    {
        var snapshot = MeshSnapshotService.EmptySnapshot();

        Assert.Equal(0, snapshot.VertexCount);
        Assert.Equal(0, snapshot.TriangleCount);
        Assert.Empty(snapshot.Positions);
        Assert.Empty(snapshot.Indices);
        Assert.Null(snapshot.Bounds);
        Assert.Null(snapshot.PaletteIndices);
    }

    [Fact]
    public void BuildSnapshot_ProducesCorrectBufferLayout()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        // Positions: 3 floats per vertex
        Assert.Equal(snapshot.VertexCount * 3, snapshot.Positions.Length);
        // Normals: 3 floats per vertex
        Assert.Equal(snapshot.VertexCount * 3, snapshot.Normals.Length);
        // Colors: 4 bytes per vertex (RGBA)
        Assert.Equal(snapshot.VertexCount * 4, snapshot.Colors.Length);
        // Palette indices: 1 byte per vertex
        Assert.Equal(snapshot.VertexCount, snapshot.PaletteIndices!.Length);
        // Indices: length is a multiple of 3
        Assert.Equal(0, snapshot.Indices.Length % 3);
    }

    [Fact]
    public void BuildSnapshot_DuplicateColors_LaterEntryWins_RenderedColorsSame()
    {
        // Two palette entries with identical RGBA colors
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "RedOne", Color = new RgbaColor(255, 0, 0, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "RedTwo", Color = new RgbaColor(255, 0, 0, 255) });
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(2, 0, 0), 2);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        // Both voxels produce the same RGBA colors in the mesh, so the palette
        // index recovery from color is ambiguous. The later entry (index 2) wins
        // in the reverse map. But the rendered colors are identical.
        Assert.NotNull(snapshot.PaletteIndices);
        Assert.Equal(snapshot.VertexCount, snapshot.PaletteIndices!.Length);

        // All vertices should have color (255, 0, 0, 255) → packed key matches index 2
        foreach (var ci in snapshot.PaletteIndices)
        {
            // Later duplicate color entry wins: index 2
            Assert.Equal((byte)2, ci);
        }

        // All vertex colors should be (255, 0, 0, 255)
        for (int i = 0; i < snapshot.VertexCount; i++)
        {
            Assert.Equal(255, snapshot.Colors[i * 4 + 0]);
            Assert.Equal(0, snapshot.Colors[i * 4 + 1]);
            Assert.Equal(0, snapshot.Colors[i * 4 + 2]);
            Assert.Equal(255, snapshot.Colors[i * 4 + 3]);
        }
    }

    [Fact]
    public void BuildSnapshot_PaletteIndicesMatchVoxelMaterial()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(2, 0, 0), 2);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        // At least some vertices should have palette index 1 (Stone)
        Assert.Contains((byte)1, snapshot.PaletteIndices!);
        // At least some vertices should have palette index 2 (Red)
        Assert.Contains((byte)2, snapshot.PaletteIndices!);
    }

    [Fact]
    public void BuildSnapshot_BoundsMatchModelBounds()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 3, 7), 1);
        model.SetVoxel(new Point3(10, 8, 15), 1);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        Assert.NotNull(snapshot.Bounds);
        Assert.Equal(5, snapshot.Bounds!.MinX);
        Assert.Equal(3, snapshot.Bounds.MinY);
        Assert.Equal(7, snapshot.Bounds.MinZ);
        Assert.Equal(10, snapshot.Bounds.MaxX);
        Assert.Equal(8, snapshot.Bounds.MaxY);
        Assert.Equal(15, snapshot.Bounds.MaxZ);
    }

    [Fact]
    public void BuildSnapshot_WithNaiveMesher_ProducesCorrectBuffers()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var service = new MeshSnapshotService(new NaiveMesher());
        var snapshot = service.BuildSnapshot(model);

        // Naive mesher: 6 faces × 4 verts = 24 verts
        Assert.Equal(24, snapshot.VertexCount);
        Assert.Equal(36, snapshot.Indices.Length);
    }

    [Fact]
    public void BuildSnapshot_GreedyAndNaive_ProduceConsistentBuffers()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(2, 2, 2), 1);

        var greedySnapshot = new MeshSnapshotService(new GreedyMesher()).BuildSnapshot(model);
        var naiveSnapshot = new MeshSnapshotService(new NaiveMesher()).BuildSnapshot(model);

        // Both should have non-zero vertices and consistent bounds
        Assert.True(greedySnapshot.VertexCount > 0);
        Assert.True(naiveSnapshot.VertexCount > 0);

        // Greedy should merge - fewer or equal triangles
        Assert.True(greedySnapshot.TriangleCount <= naiveSnapshot.TriangleCount);

        // Both should have same bounds
        Assert.NotNull(greedySnapshot.Bounds);
        Assert.NotNull(naiveSnapshot.Bounds);
        Assert.Equal(greedySnapshot.Bounds!.MinX, naiveSnapshot.Bounds!.MinX);
        Assert.Equal(greedySnapshot.Bounds.MaxX, naiveSnapshot.Bounds.MaxX);
    }

    [Fact]
    public void BuildSnapshot_PositionsAndNormalsAreFinite()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(4, 4, 4), 1);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        foreach (float f in snapshot.Positions)
            Assert.False(float.IsNaN(f) || float.IsInfinity(f), "Position value should be finite");
        foreach (float f in snapshot.Normals)
            Assert.False(float.IsNaN(f) || float.IsInfinity(f), "Normal value should be finite");
    }

    [Fact]
    public void BuildSnapshot_AllIndicesInBounds()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        model.SetVoxel(new Point3(0, 1, 0), 1);

        var service = new MeshSnapshotService(new GreedyMesher());
        var snapshot = service.BuildSnapshot(model);

        foreach (int idx in snapshot.Indices)
        {
            Assert.True(idx >= 0 && idx < snapshot.VertexCount,
                $"Index {idx} out of range [0, {snapshot.VertexCount})");
        }
    }
}