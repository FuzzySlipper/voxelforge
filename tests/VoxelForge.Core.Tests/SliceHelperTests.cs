using Microsoft.Extensions.Logging.Abstractions;

namespace VoxelForge.Core.Tests;

public sealed class SliceHelperTests
{
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    [Fact]
    public void SliceToWorld_ZAxis_MapsCorrectly()
    {
        var result = SliceHelper.SliceToWorld(SliceAxis.Z, 5, 3, 7);
        Assert.Equal(new Point3(3, 7, 5), result);
    }

    [Fact]
    public void SliceToWorld_YAxis_MapsCorrectly()
    {
        var result = SliceHelper.SliceToWorld(SliceAxis.Y, 10, 3, 7);
        Assert.Equal(new Point3(3, 10, 7), result);
    }

    [Fact]
    public void SliceToWorld_XAxis_MapsCorrectly()
    {
        var result = SliceHelper.SliceToWorld(SliceAxis.X, 2, 4, 6);
        Assert.Equal(new Point3(2, 4, 6), result);
    }

    [Fact]
    public void PixelToCell_ValidPosition_ReturnsCorrectCell()
    {
        // Cell size 8, offset (10, 20), grid 32
        var result = SliceHelper.PixelToCell(26, 36, 8, 10, 20, 32);
        Assert.NotNull(result);
        Assert.Equal(2, result.Value.U); // (26-10)/8 = 2
        Assert.Equal(2, result.Value.V); // (36-20)/8 = 2
    }

    [Fact]
    public void PixelToCell_OutOfBounds_ReturnsNull()
    {
        var result = SliceHelper.PixelToCell(500, 500, 8, 0, 0, 32);
        Assert.Null(result);
    }

    [Fact]
    public void PixelToCell_NegativePosition_ReturnsNull()
    {
        var result = SliceHelper.PixelToCell(-5, 10, 8, 0, 0, 32);
        Assert.Null(result);
    }

    [Fact]
    public void GetSliceVoxel_ReturnsCorrectValue()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(3, 7, 5), 42);

        // Z-axis slice at layer 5, position (3, 7)
        var result = SliceHelper.GetSliceVoxel(model, SliceAxis.Z, 5, 3, 7);
        Assert.Equal((byte)42, result);
    }

    [Fact]
    public void GetSliceVoxel_AirPosition_ReturnsNull()
    {
        var model = CreateModel();
        var result = SliceHelper.GetSliceVoxel(model, SliceAxis.Z, 5, 0, 0);
        Assert.Null(result);
    }

    [Fact]
    public void GetSliceAxes_ZAxis_ReturnsXY()
    {
        var (u, v) = SliceHelper.GetSliceAxes(SliceAxis.Z);
        Assert.Equal(0, u); // X
        Assert.Equal(1, v); // Y
    }

    [Fact]
    public void GetSliceAxes_YAxis_ReturnsXZ()
    {
        var (u, v) = SliceHelper.GetSliceAxes(SliceAxis.Y);
        Assert.Equal(0, u); // X
        Assert.Equal(2, v); // Z
    }

    [Fact]
    public void GetSliceAxes_XAxis_ReturnsYZ()
    {
        var (u, v) = SliceHelper.GetSliceAxes(SliceAxis.X);
        Assert.Equal(1, u); // Y
        Assert.Equal(2, v); // Z
    }
}
