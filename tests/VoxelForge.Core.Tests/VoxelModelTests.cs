using Microsoft.Extensions.Logging.Abstractions;

namespace VoxelForge.Core.Tests;

public sealed class VoxelModelTests
{
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    [Fact]
    public void SetAndGetVoxel_ReturnsCorrectPaletteIndex()
    {
        var model = CreateModel();

        for (int i = 0; i < 100; i++)
        {
            var pos = new Point3(i, i + 1, i + 2);
            byte index = (byte)((i % 254) + 1);
            model.SetVoxel(pos, index);
        }

        for (int i = 0; i < 100; i++)
        {
            var pos = new Point3(i, i + 1, i + 2);
            byte expected = (byte)((i % 254) + 1);
            Assert.Equal(expected, model.GetVoxel(pos));
        }
    }

    [Fact]
    public void SetVoxel_OverwritesExistingValue()
    {
        var model = CreateModel();
        var pos = new Point3(5, 5, 5);

        model.SetVoxel(pos, 1);
        Assert.Equal((byte)1, model.GetVoxel(pos));

        model.SetVoxel(pos, 2);
        Assert.Equal((byte)2, model.GetVoxel(pos));
    }

    [Fact]
    public void GetVoxel_AirPosition_ReturnsNull()
    {
        var model = CreateModel();
        Assert.Null(model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public void RemoveVoxel_ExistingVoxel_RemovesIt()
    {
        var model = CreateModel();
        var pos = new Point3(1, 2, 3);
        model.SetVoxel(pos, 1);

        model.RemoveVoxel(pos);

        Assert.Null(model.GetVoxel(pos));
        Assert.Equal(0, model.GetVoxelCount());
    }

    [Fact]
    public void RemoveVoxel_NonExistent_DoesNotThrow()
    {
        var model = CreateModel();
        var exception = Record.Exception(() => model.RemoveVoxel(new Point3(99, 99, 99)));
        Assert.Null(exception);
    }

    [Fact]
    public void FillRegion_CreatesCorrectVoxelCount()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(31, 31, 31), 1);
        Assert.Equal(32 * 32 * 32, model.GetVoxelCount());
    }

    [Fact]
    public void FillRegion_AllVoxelsHaveCorrectIndex()
    {
        var model = CreateModel();
        model.FillRegion(new Point3(0, 0, 0), new Point3(3, 3, 3), 7);

        for (int x = 0; x <= 3; x++)
        for (int y = 0; y <= 3; y++)
        for (int z = 0; z <= 3; z++)
            Assert.Equal((byte)7, model.GetVoxel(new Point3(x, y, z)));
    }

    [Fact]
    public void GetBounds_EmptyModel_ReturnsNull()
    {
        var model = CreateModel();
        Assert.Null(model.GetBounds());
    }

    [Fact]
    public void GetBounds_ReturnsCorrectBounds()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 10, 3), 1);
        model.SetVoxel(new Point3(20, 2, 15), 1);

        var bounds = model.GetBounds();

        Assert.NotNull(bounds);
        Assert.Equal(new Point3(5, 2, 3), bounds.Value.Min);
        Assert.Equal(new Point3(20, 10, 15), bounds.Value.Max);
    }

    [Fact]
    public void GetBounds_SingleVoxel_MinEqualsMax()
    {
        var model = CreateModel();
        var pos = new Point3(7, 7, 7);
        model.SetVoxel(pos, 1);

        var bounds = model.GetBounds();

        Assert.NotNull(bounds);
        Assert.Equal(pos, bounds.Value.Min);
        Assert.Equal(pos, bounds.Value.Max);
    }

    [Fact]
    public void GridHint_DefaultsTo32()
    {
        var model = CreateModel();
        Assert.Equal(32, model.GridHint);
    }
}
