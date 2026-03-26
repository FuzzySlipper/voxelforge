namespace VoxelForge.Core.Tests;

public sealed class PaletteTests
{
    [Fact]
    public void Set_And_Get_ReturnsCorrectMaterial()
    {
        var palette = new Palette();
        var mat = new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) };

        palette.Set(1, mat);

        var result = palette.Get(1);
        Assert.NotNull(result);
        Assert.Equal("Stone", result.Name);
        Assert.Equal(new RgbaColor(128, 128, 128), result.Color);
    }

    [Fact]
    public void Get_UnsetIndex_ReturnsNull()
    {
        var palette = new Palette();
        Assert.Null(palette.Get(42));
    }

    [Fact]
    public void Set_IndexZero_IsIgnored()
    {
        var palette = new Palette();
        var mat = new MaterialDef { Name = "Air", Color = new RgbaColor(0, 0, 0, 0) };

        palette.Set(0, mat);

        Assert.Null(palette.Get(0));
        Assert.Equal(0, palette.Count);
    }

    [Fact]
    public void Set_OverwritesExistingEntry()
    {
        var palette = new Palette();
        palette.Set(1, new MaterialDef { Name = "Old", Color = RgbaColor.White });
        palette.Set(1, new MaterialDef { Name = "New", Color = RgbaColor.Magenta });

        Assert.Equal("New", palette.Get(1)!.Name);
        Assert.Equal(1, palette.Count);
    }

    [Fact]
    public void Contains_ReturnsTrueForSetIndex()
    {
        var palette = new Palette();
        palette.Set(5, new MaterialDef { Name = "Test", Color = RgbaColor.White });

        Assert.True(palette.Contains(5));
        Assert.False(palette.Contains(6));
    }

    [Fact]
    public void Entries_ReturnsAllSetEntries()
    {
        var palette = new Palette();
        palette.Set(1, new MaterialDef { Name = "A", Color = RgbaColor.White });
        palette.Set(2, new MaterialDef { Name = "B", Color = RgbaColor.Magenta });

        Assert.Equal(2, palette.Entries.Count);
        Assert.Equal("A", palette.Entries[1].Name);
        Assert.Equal("B", palette.Entries[2].Name);
    }
}
