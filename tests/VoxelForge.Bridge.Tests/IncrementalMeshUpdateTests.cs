using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.Bridge.Tests;

public sealed class MeshRegionServiceTests
{
    private static VoxelModel CreateTestModel()
    {
        var model = new VoxelModel(Microsoft.Extensions.Logging.Abstractions.NullLogger<VoxelModel>.Instance)
        {
            GridHint = 16,
        };

        model.Palette.Set(1, new MaterialDef
        {
            Name = "Stone",
            Color = new RgbaColor(160, 160, 160, 255),
        });

        // Fill a 3x3x3 cube at origin
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
        for (int z = 0; z < 3; z++)
            model.SetVoxel(new Point3(x, y, z), (byte)1);

        return model;
    }

    [Fact]
    public void GetOccupiedRegions_SingleChunk_ReturnsOneRegion()
    {
        var model = CreateTestModel();
        var regions = MeshRegionService.GetOccupiedRegions(model, chunkSize: 16);

        Assert.Single(regions);
        Assert.Contains(new MeshRegionCoord(0, 0, 0), regions);
    }

    [Fact]
    public void GetOccupiedRegions_EmptyModel_ReturnsNoRegions()
    {
        var model = new VoxelModel(Microsoft.Extensions.Logging.Abstractions.NullLogger<VoxelModel>.Instance);
        var regions = MeshRegionService.GetOccupiedRegions(model, chunkSize: 16);

        Assert.Empty(regions);
    }

    [Fact]
    public void GetOccupiedRegions_LargeChunkSize_AllVoxelsInOneRegion()
    {
        var model = CreateTestModel();
        var regions = MeshRegionService.GetOccupiedRegions(model, chunkSize: 64);

        Assert.Single(regions);
        Assert.Contains(new MeshRegionCoord(0, 0, 0), regions);
    }

    [Fact]
    public void GetOccupiedRegions_SmallChunkSize_MultipleRegions()
    {
        var model = new VoxelModel(Microsoft.Extensions.Logging.Abstractions.NullLogger<VoxelModel>.Instance)
        {
            GridHint = 4,
        };
        model.Palette.Set(1, new MaterialDef { Name = "A", Color = new RgbaColor(255, 0, 0, 255) });
        model.Palette.Set(2, new MaterialDef { Name = "B", Color = new RgbaColor(0, 255, 0, 255) });

        // Voxels in two different 2x2x2 chunks
        model.SetVoxel(new Point3(0, 0, 0), (byte)1);
        model.SetVoxel(new Point3(5, 5, 5), (byte)2);

        var regions = MeshRegionService.GetOccupiedRegions(model, chunkSize: 2);

        Assert.Equal(2, regions.Count);
        Assert.Contains(new MeshRegionCoord(0, 0, 0), regions);
        Assert.Contains(new MeshRegionCoord(2, 2, 2), regions);
    }

    [Fact]
    public void GetAffectedRegions_SinglePoint_ReturnsOneRegion()
    {
        var regions = MeshRegionService.GetAffectedRegions(
            new Point3(5, 10, 15),
            new Point3(5, 10, 15),
            chunkSize: 16);

        Assert.Single(regions);
        Assert.Contains(new MeshRegionCoord(0, 0, 0), regions);
    }

    [Fact]
    public void GetAffectedRegions_AcrossChunkBoundary_ReturnsMultipleRegions()
    {
        // Change spanning (15,15,15) to (17,17,17) crosses the 16-chunk boundary
        var regions = MeshRegionService.GetAffectedRegions(
            new Point3(15, 15, 15),
            new Point3(17, 17, 17),
            chunkSize: 16);

        // Should cover multiple regions across boundaries
        Assert.True(regions.Count >= 2, "Change across chunk boundary should affect multiple regions");
    }

    [Fact]
    public void GetAffectedRegions_NegativeCoordinates_CorrectFloorDiv()
    {
        // Test that negative coordinates use floor division (not truncation)
        var regions = MeshRegionService.GetAffectedRegions(
            new Point3(-1, -1, -1),
            new Point3(-1, -1, -1),
            chunkSize: 16);

        Assert.Single(regions);
        Assert.Contains(new MeshRegionCoord(-1, -1, -1), regions);
    }

    [Fact]
    public void MeshRegionCoord_ToBounds_ReturnsCorrectBounds()
    {
        var coord = new MeshRegionCoord(0, 0, 0);
        var bounds = coord.ToBounds(chunkSize: 16);

        Assert.Equal(0, bounds.MinX);
        Assert.Equal(0, bounds.MinY);
        Assert.Equal(0, bounds.MinZ);
        Assert.Equal(15, bounds.MaxX);
        Assert.Equal(15, bounds.MaxY);
        Assert.Equal(15, bounds.MaxZ);
    }

    [Fact]
    public void MeshRegionCoord_ToBounds_NegativeCoord_CorrectBounds()
    {
        var coord = new MeshRegionCoord(-1, 0, 0);
        var bounds = coord.ToBounds(chunkSize: 16);

        Assert.Equal(-16, bounds.MinX);
        Assert.Equal(-1, bounds.MaxX);
    }

    [Fact]
    public void BuildIncrementalUpdate_FullReplace_NoDirtyRegions()
    {
        var model = CreateTestModel();
        var mesher = new VoxelForge.Core.Meshing.GreedyMesher();
        var service = new MeshRegionService(mesher);

        // No dirty regions → full replace
        var update = service.BuildIncrementalUpdate(model, [], "test-model", "mesh-test-001");

        Assert.Equal("test-model", update.ModelId);
        Assert.Equal(MeshUpdateType.FullReplace, update.UpdateType);
        Assert.Single(update.ChangedRegions);
        Assert.Equal("all", update.ChangedRegions[0].RegionId);
        Assert.Equal(MeshRegionUpdateKind.FullReplace, update.ChangedRegions[0].UpdateKind);
    }

    [Fact]
    public void BuildIncrementalUpdate_WithDirtyRegions_ContainsRegionData()
    {
        var model = CreateTestModel();
        var mesher = new VoxelForge.Core.Meshing.GreedyMesher();
        var service = new MeshRegionService(mesher);

        var dirtyRegions = new HashSet<MeshRegionCoord> { new(0, 0, 0) };
        var update = service.BuildIncrementalUpdate(model, dirtyRegions, "test-model", "mesh-test-001");

        Assert.Equal(MeshUpdateType.Incremental, update.UpdateType);
        Assert.Single(update.ChangedRegions);

        var region = update.ChangedRegions[0];
        Assert.Equal("0_0_0", region.RegionId);
        Assert.Equal(MeshRegionUpdateKind.Incremental, region.UpdateKind);
        Assert.True(region.VertexCount > 0, "Region should contain vertices");
        Assert.True(region.IndexCount > 0, "Region should contain indices");
        Assert.Equal(region.VertexCount * 3, region.Positions.Length);
        Assert.Equal(region.VertexCount * 3, region.Normals.Length);
        Assert.Equal(region.VertexCount * 4, region.Colors.Length);
    }

    [Fact]
    public void BuildIncrementalUpdate_EmptyRegion_ReturnsZeroVertices()
    {
        var model = CreateTestModel();
        var mesher = new VoxelForge.Core.Meshing.GreedyMesher();
        var service = new MeshRegionService(mesher);

        // Region that has no voxels — far from the model
        var dirtyRegions = new HashSet<MeshRegionCoord> { new(10, 10, 10) };
        var update = service.BuildIncrementalUpdate(model, dirtyRegions, "test-model", "mesh-test-001");

        Assert.Equal(MeshUpdateType.Incremental, update.UpdateType);
        Assert.Single(update.ChangedRegions);

        var region = update.ChangedRegions[0];
        Assert.Equal("10_10_10", region.RegionId);
        Assert.Equal(0, region.VertexCount);
        Assert.Equal(0, region.IndexCount);
    }

    [Fact]
    public void BuildIncrementalUpdate_PaletteIndicesIncluded_WhenPresent()
    {
        var model = CreateTestModel();
        var mesher = new VoxelForge.Core.Meshing.GreedyMesher();
        var service = new MeshRegionService(mesher);

        var dirtyRegions = new HashSet<MeshRegionCoord> { new(0, 0, 0) };
        var update = service.BuildIncrementalUpdate(model, dirtyRegions, "test-model", "mesh-test-001");

        var region = update.ChangedRegions[0];
        Assert.NotNull(region.PaletteIndices);
        Assert.Equal(region.VertexCount, region.PaletteIndices.Length);
    }

    [Fact]
    public void ChunkSize_Zero_ThrowsArgumentOutOfRangeException()
    {
        var model = CreateTestModel();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MeshRegionService.GetOccupiedRegions(model, chunkSize: 0));
    }

    [Fact]
    public void MeshRegionCoord_FromPoint_ZeroOrigin()
    {
        var coord = MeshRegionCoord.FromPoint(new Point3(0, 0, 0), chunkSize: 16);
        Assert.Equal(new MeshRegionCoord(0, 0, 0), coord);
    }

    [Fact]
    public void MeshRegionCoord_FromPoint_PositiveCoord()
    {
        var coord = MeshRegionCoord.FromPoint(new Point3(31, 15, 47), chunkSize: 16);
        Assert.Equal(new MeshRegionCoord(1, 0, 2), coord);
    }

    [Fact]
    public void MeshRegionCoord_FromPoint_NegativeCoord()
    {
        var coord = MeshRegionCoord.FromPoint(new Point3(-1, -16, -17), chunkSize: 16);
        Assert.Equal(new MeshRegionCoord(-1, -1, -2), coord);
    }
}

public sealed class MeshSubscriptionManagerTests
{
    [Fact]
    public void Subscribe_ReturnsSubscriptionWithId()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        var sub = manager.Subscribe("test-model", 16, "req-1");

        Assert.NotNull(sub);
        Assert.False(string.IsNullOrEmpty(sub.SubscriptionId));
        Assert.Equal("test-model", sub.ModelId);
        Assert.Equal(16, sub.ChunkSize);
        Assert.Equal("req-1", sub.RequestId);
    }

    [Fact]
    public void Unsubscribe_RemovesSubscription()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        var sub = manager.Subscribe("test-model", 16, "req-1");
        manager.Unsubscribe(sub.SubscriptionId);

        Assert.Equal(0, manager.GetSubscriptionCount("test-model"));
    }

    [Fact]
    public void GetSubscriptions_ReturnsActiveSubscriptions()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        manager.Subscribe("model-a", 16, "req-1");
        manager.Subscribe("model-a", 16, "req-2");
        manager.Subscribe("model-b", 16, "req-3");

        var subsA = manager.GetSubscriptions("model-a");
        var subsB = manager.GetSubscriptions("model-b");

        Assert.Equal(2, subsA.Count);
        Assert.Single(subsB);
    }

    [Fact]
    public void RecordDirtyRegions_TracksRegions()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        var regions = new HashSet<MeshRegionCoord>
        {
            new(0, 0, 0),
            new(1, 0, 0),
        };

        manager.RecordDirtyRegions("test-model", regions);

        var consumed = manager.ConsumeDirtyRegions("test-model");
        Assert.NotNull(consumed);
        Assert.Equal(2, consumed.Count);
    }

    [Fact]
    public void ConsumeDirtyRegions_ClearsState()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        manager.RecordDirtyRegions("test-model", [new MeshRegionCoord(0, 0, 0)]);
        var first = manager.ConsumeDirtyRegions("test-model");

        Assert.NotNull(first);

        var second = manager.ConsumeDirtyRegions("test-model");
        Assert.Null(second);
    }

    [Fact]
    public void RecordFullDirty_TracksAllOccupiedRegions()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();
        var model = new VoxelModel(Microsoft.Extensions.Logging.Abstractions.NullLogger<VoxelModel>.Instance)
        {
            GridHint = 16,
        };
        model.Palette.Set(1, new MaterialDef { Name = "A", Color = new RgbaColor(255, 0, 0, 255) });
        model.SetVoxel(new Point3(0, 0, 0), (byte)1);
        model.SetVoxel(new Point3(20, 0, 0), (byte)1);

        manager.RecordFullDirty("test-model", model);

        var dirty = manager.ConsumeDirtyRegions("test-model");
        Assert.NotNull(dirty);
        Assert.Equal(2, dirty.Count); // Two separate chunks
    }

    [Fact]
    public void NextSequence_IsMonotonicallyIncreasing()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();

        var seq1 = manager.NextSequence();
        var seq2 = manager.NextSequence();
        var seq3 = manager.NextSequence();

        Assert.True(seq1 < seq2);
        Assert.True(seq2 < seq3);
    }

    [Fact]
    public void GetSubscriptionCount_ReturnsZeroForNoSubscriptions()
    {
        var manager = new VoxelForge.Bridge.Handlers.MeshSubscriptionManager();
        Assert.Equal(0, manager.GetSubscriptionCount("nonexistent"));
    }
}