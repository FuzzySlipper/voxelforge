using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core.Voxelization;

namespace VoxelForge.Core.Tests;

public sealed class VoxelizeServiceTests
{
    private static TriangleMesh MakeCube(float size = 1f)
    {
        float h = size / 2f;
        var positions = new Vector3[]
        {
            new(-h, -h, -h), new(h, -h, -h), new(h, h, -h), new(-h, h, -h),
            new(-h, -h,  h), new(h, -h,  h), new(h, h,  h), new(-h, h,  h),
        };

        var indices = new int[]
        {
            // Front
            0, 2, 1, 0, 3, 2,
            // Back
            4, 5, 6, 4, 6, 7,
            // Left
            0, 4, 7, 0, 7, 3,
            // Right
            1, 2, 6, 1, 6, 5,
            // Top
            3, 7, 6, 3, 6, 2,
            // Bottom
            0, 1, 5, 0, 5, 4,
        };

        return new TriangleMesh { Positions = positions, Indices = indices };
    }

    [Fact]
    public void RayTriangleIntersect_HittingTriangle_ReturnsTrue()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        var origin = new Vector3(0.2f, 0.2f, -1f);
        var dir = new Vector3(0, 0, 1);

        bool hit = VoxelizeService.RayTriangleIntersect(origin, dir, v0, v1, v2, out float t);

        Assert.True(hit);
        Assert.True(t > 0);
    }

    [Fact]
    public void RayTriangleIntersect_MissingTriangle_ReturnsFalse()
    {
        var v0 = new Vector3(0, 0, 0);
        var v1 = new Vector3(1, 0, 0);
        var v2 = new Vector3(0, 1, 0);

        var origin = new Vector3(5f, 5f, -1f);
        var dir = new Vector3(0, 0, 1);

        bool hit = VoxelizeService.RayTriangleIntersect(origin, dir, v0, v1, v2, out _);

        Assert.False(hit);
    }

    [Fact]
    public void Voxelize_Cube_Solid_ProducesVoxels()
    {
        var mesh = MakeCube(2f);
        var service = new VoxelizeService(NullLoggerFactory.Instance);

        var result = service.Voxelize(mesh, 8, VoxelizeMode.Solid);

        Assert.True(result.GetVoxelCount() > 0, "Solid voxelization should produce voxels");
        // A 2x2x2 cube voxelized at resolution 8 should fill a good portion
        Assert.True(result.GetVoxelCount() > 50, $"Expected >50 voxels, got {result.GetVoxelCount()}");
    }

    [Fact]
    public void Voxelize_Cube_Surface_ProducesFewer_ThanSolid()
    {
        var mesh = MakeCube(2f);
        var service = new VoxelizeService(NullLoggerFactory.Instance);

        var solid = service.Voxelize(mesh, 8, VoxelizeMode.Solid);
        var surface = service.Voxelize(mesh, 8, VoxelizeMode.Surface);

        Assert.True(surface.GetVoxelCount() > 0, "Surface voxelization should produce voxels");
        Assert.True(surface.GetVoxelCount() <= solid.GetVoxelCount(),
            $"Surface ({surface.GetVoxelCount()}) should have <= voxels than Solid ({solid.GetVoxelCount()})");
    }

    [Fact]
    public void Voxelize_ResultHasCorrectGridHint()
    {
        var mesh = MakeCube(1f);
        var service = new VoxelizeService(NullLoggerFactory.Instance);

        var result = service.Voxelize(mesh, 16, VoxelizeMode.Solid);

        Assert.Equal(16, result.GridHint);
    }

    [Fact]
    public void Voxelize_UsesSpecifiedPaletteIndex()
    {
        var mesh = MakeCube(1f);
        var service = new VoxelizeService(NullLoggerFactory.Instance);

        var result = service.Voxelize(mesh, 8, VoxelizeMode.Solid, paletteIndex: 5);

        var firstVoxel = result.Voxels.Values.First();
        Assert.Equal((byte)5, firstVoxel);
    }
}
