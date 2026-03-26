using Microsoft.Extensions.Logging.Abstractions;

namespace VoxelForge.Core.Tests;

public sealed class RaycastTests
{
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    [Fact]
    public void RayDown_HitsTopFace()
    {
        var model = CreateModel();
        // Place a voxel at (5, 9, 5) — occupies [5,10) x [9,10) x [5,6)
        model.SetVoxel(new Point3(5, 9, 5), 1);

        // Ray pointing straight down from above
        var hit = VoxelRaycaster.Cast(model,
            5.5f, 20f, 5.5f,  // origin above the voxel
            0f, -1f, 0f);      // direction: straight down

        Assert.NotNull(hit);
        Assert.Equal(new Point3(5, 9, 5), hit.Value.VoxelPos);
        Assert.Equal(new Point3(0, 1, 0), hit.Value.FaceNormal); // hit from above
    }

    [Fact]
    public void RayMissesAllVoxels_ReturnsNull()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 5, 5), 1);

        // Ray that misses entirely
        var hit = VoxelRaycaster.Cast(model,
            100f, 100f, 100f,
            1f, 0f, 0f);

        Assert.Null(hit);
    }

    [Fact]
    public void RayFromSide_HitsCorrectFace()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 5, 5), 1);

        // Ray from -X direction
        var hit = VoxelRaycaster.Cast(model,
            0f, 5.5f, 5.5f,
            1f, 0f, 0f);

        Assert.NotNull(hit);
        Assert.Equal(new Point3(5, 5, 5), hit.Value.VoxelPos);
        Assert.Equal(new Point3(-1, 0, 0), hit.Value.FaceNormal); // hit from -X side
    }

    [Fact]
    public void RayFromFront_HitsZFace()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 5, 5), 1);

        // Ray from -Z direction
        var hit = VoxelRaycaster.Cast(model,
            5.5f, 5.5f, 0f,
            0f, 0f, 1f);

        Assert.NotNull(hit);
        Assert.Equal(new Point3(5, 5, 5), hit.Value.VoxelPos);
        Assert.Equal(new Point3(0, 0, -1), hit.Value.FaceNormal);
    }

    [Fact]
    public void Ray_HitsNearestVoxelFirst()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(3, 5, 5), 1);
        model.SetVoxel(new Point3(8, 5, 5), 1);

        var hit = VoxelRaycaster.Cast(model,
            0f, 5.5f, 5.5f,
            1f, 0f, 0f);

        Assert.NotNull(hit);
        Assert.Equal(new Point3(3, 5, 5), hit.Value.VoxelPos); // nearer voxel
    }

    [Fact]
    public void EmptyModel_ReturnsNull()
    {
        var model = CreateModel();
        var hit = VoxelRaycaster.Cast(model, 0f, 0f, 0f, 1f, 0f, 0f);
        Assert.Null(hit);
    }

    [Fact]
    public void PlaceTool_AddsVoxelAtHitFaceNormal()
    {
        // Integration test: simulate what PlaceTool does
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 0, 5), 1);

        var hit = VoxelRaycaster.Cast(model,
            5.5f, 10f, 5.5f,
            0f, -1f, 0f);

        Assert.NotNull(hit);

        // PlaceTool logic: new voxel at hit pos + face normal
        var newPos = new Point3(
            hit.Value.VoxelPos.X + hit.Value.FaceNormal.X,
            hit.Value.VoxelPos.Y + hit.Value.FaceNormal.Y,
            hit.Value.VoxelPos.Z + hit.Value.FaceNormal.Z);

        Assert.Equal(new Point3(5, 1, 5), newPos); // one above
    }
}
