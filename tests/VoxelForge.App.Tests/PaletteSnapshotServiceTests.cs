using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Tests;

public sealed class PaletteSnapshotServiceTests
{
    [Fact]
    public void BuildSnapshot_ReturnsPaletteEntries()
    {
        var palette = new Palette();
        palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        palette.Set(2, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0) });

        var service = new PaletteSnapshotService();
        var snapshot = service.BuildSnapshot(palette);

        Assert.Equal(2, snapshot.EntryCount);
        Assert.Equal(2, snapshot.Entries.Count);

        // Entries should be sorted by index
        Assert.Equal((byte)1, snapshot.Entries[0].Index);
        Assert.Equal("Stone", snapshot.Entries[0].Name);
        Assert.Equal(128, snapshot.Entries[0].R);
        Assert.Equal((byte)2, snapshot.Entries[1].Index);
        Assert.Equal("Red", snapshot.Entries[1].Name);
    }

    [Fact]
    public void BuildSnapshot_EmptyPalette_ReturnsEmptyEntries()
    {
        var palette = new Palette();
        var service = new PaletteSnapshotService();
        var snapshot = service.BuildSnapshot(palette);

        Assert.Equal(0, snapshot.EntryCount);
        Assert.Empty(snapshot.Entries);
    }

    [Fact]
    public void BuildSnapshot_ExcludesAirIndex()
    {
        var palette = new Palette();
        // Setting index 0 should be ignored (it's reserved for air)
        palette.Set(0, new MaterialDef { Name = "Air", Color = new RgbaColor(0, 0, 0, 0) });
        palette.Set(1, new MaterialDef { Name = "Dirt", Color = new RgbaColor(139, 90, 43) });

        var service = new PaletteSnapshotService();
        var snapshot = service.BuildSnapshot(palette);

        // Palette.Set(0, ...) is silently ignored, so only entry 1 should appear
        Assert.Equal(1, snapshot.EntryCount);
        Assert.Equal((byte)1, snapshot.Entries[0].Index);
    }

    [Fact]
    public void BuildSnapshot_ColorComponentsPreserved()
    {
        var palette = new Palette();
        palette.Set(5, new MaterialDef { Name = "TransparentBlue", Color = new RgbaColor(0, 0, 255, 128) });

        var service = new PaletteSnapshotService();
        var snapshot = service.BuildSnapshot(palette);

        Assert.Single(snapshot.Entries);
        var entry = snapshot.Entries[0];
        Assert.Equal((byte)5, entry.Index);
        Assert.Equal(0, entry.R);
        Assert.Equal(0, entry.G);
        Assert.Equal(255, entry.B);
        Assert.Equal(128, entry.A);
    }
}