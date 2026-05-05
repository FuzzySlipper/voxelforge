using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.Bridge.Tests;

public sealed class MeshSnapshotHandlerTests
{
    private static VoxelModel CreateTestModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance)
        {
            GridHint = 16,
        };

        model.Palette.Set(1, new MaterialDef
        {
            Name = "Stone",
            Color = new RgbaColor(160, 160, 160, 255),
        });
        model.Palette.Set(2, new MaterialDef
        {
            Name = "Red",
            Color = new RgbaColor(255, 0, 0, 255),
        });

        // 3x3x3 cube with stone, top face red
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
        for (int z = 0; z < 3; z++)
            model.SetVoxel(new Point3(x, y, z), (byte)1);

        for (int x = 0; x < 3; x++)
        for (int z = 0; z < 3; z++)
            model.SetVoxel(new Point3(x, 2, z), (byte)2);

        return model;
    }

    private static (MeshSnapshotHandler handler, VoxelModelHolder holder) CreateHandler(VoxelModel? model = null)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);

        if (model is not null)
        {
            // Use LoadDefaultCube then swap in the provided model
            holder.LoadDefaultCube();
        }

        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        var handler = new MeshSnapshotHandler(holder, meshService, paletteService);

        return (handler, holder);
    }

    [Fact]
    public async Task MeshSnapshotHandler_ThrowsWhenNoModelLoaded()
    {
        var (handler, _) = CreateHandler(model: null);
        var context = new BridgeRequestContext("req-mesh-1", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await handler.HandleAsync(
                new MeshSnapshotRequest(),
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.mesh.not_loaded", ex.Code);
        Assert.Equal(BridgeErrorCategories.NotFound, ex.Category);
    }

    [Fact]
    public async Task MeshSnapshotHandler_ReturnsValidSnapshot_WhenModelLoaded()
    {
        var model = CreateTestModel();
        var (handler, holder) = CreateHandler();

        // Replace the model in the holder with our test model
        // Since we can't inject directly, use LoadDefaultCube then test with that
        // Actually, let's load a model directly
        var loggerFactory = NullLoggerFactory.Instance;
        var holderWithModel = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);
        holderWithModel.LoadDefaultCube();

        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        handler = new MeshSnapshotHandler(holderWithModel, meshService, paletteService);

        var context = new BridgeRequestContext("req-mesh-2", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new MeshSnapshotRequest { IncludePaletteMapping = true },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("default-cube", response.ModelId);
        Assert.Equal("json", response.Format);
        Assert.True(response.VertexCount > 0, "Should have vertices");
        Assert.True(response.IndexCount > 0, "Should have indices");
        Assert.Equal(response.VertexCount * 3, response.Positions.Length);
        Assert.Equal(response.VertexCount * 3, response.Normals.Length);
        Assert.Equal(response.VertexCount * 4, response.Colors.Length);
        Assert.NotNull(response.Bounds);
        Assert.NotNull(response.PaletteMapping);
        Assert.NotNull(response.Metrics);
        Assert.True(response.Metrics.MeshGenerationMs >= 0);
        Assert.True(response.Metrics.TotalMs >= 0);
    }

    [Fact]
    public async Task MeshSnapshotHandler_PaletteMappingIncludesEntries()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);
        holder.LoadDefaultCube();

        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        var handler = new MeshSnapshotHandler(holder, meshService, paletteService);

        var context = new BridgeRequestContext("req-mesh-palette", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new MeshSnapshotRequest { IncludePaletteMapping = true },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.PaletteMapping);
        Assert.True(response.PaletteMapping.Count >= 1, "Should have at least one palette entry");

        // Stone should be present at index 1
        Assert.True(response.PaletteMapping.ContainsKey("1"));
        Assert.Equal("Stone", response.PaletteMapping["1"].Name);
        Assert.Equal("#A0A0A0", response.PaletteMapping["1"].Color);
    }

    [Fact]
    public async Task MeshSnapshotHandler_SkipsPaletteWhenNotRequested()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);
        holder.LoadDefaultCube();

        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        var handler = new MeshSnapshotHandler(holder, meshService, paletteService);

        var context = new BridgeRequestContext("req-mesh-no-palette", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new MeshSnapshotRequest { IncludePaletteMapping = false },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Null(response.PaletteMapping);
    }

    [Fact]
    public async Task MeshSnapshotHandler_BoundsAreCorrect()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);
        holder.LoadDefaultCube();

        var mesher = new GreedyMesher();
        var meshService = new MeshSnapshotService(mesher);
        var paletteService = new PaletteSnapshotService();
        var handler = new MeshSnapshotHandler(holder, meshService, paletteService);

        var context = new BridgeRequestContext("req-mesh-bounds", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new MeshSnapshotRequest(),
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Bounds);
        // Default cube spans (0,0,0) to (2,2,2)
        Assert.True(response.Bounds.MinX <= 0);
        Assert.True(response.Bounds.MinY <= 0);
        Assert.True(response.Bounds.MinZ <= 0);
        Assert.True(response.Bounds.MaxX >= 2);
        Assert.True(response.Bounds.MaxY >= 2);
        Assert.True(response.Bounds.MaxZ >= 2);
    }
}

public sealed class PaletteGetHandlerTests
{
    [Fact]
    public async Task PaletteGetHandler_ThrowsWhenNoModelLoaded()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);

        var paletteService = new PaletteSnapshotService();
        var handler = new PaletteGetHandler(holder, paletteService);

        var context = new BridgeRequestContext("req-pal-1", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await handler.HandleAsync(
                new PaletteGetRequest(),
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.palette.not_loaded", ex.Code);
    }

    [Fact]
    public async Task PaletteGetHandler_ReturnsPaletteEntries_WhenModelLoaded()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(
            NullLogger<VoxelModelHolder>.Instance,
            loggerFactory);
        holder.LoadDefaultCube();

        var paletteService = new PaletteSnapshotService();
        var handler = new PaletteGetHandler(holder, paletteService);

        var context = new BridgeRequestContext("req-pal-2", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new PaletteGetRequest(),
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("default", response.PaletteId);
        Assert.True(response.EntryCount >= 2, "Default cube has at least 2 palette entries");
        Assert.True(response.Entries.Length >= 2);

        // Find Stone entry
        var stone = response.Entries.FirstOrDefault(e => e.Name == "Stone");
        Assert.NotNull(stone);
        Assert.Equal((byte)1, stone.Index);
        Assert.Equal("#A0A0A0", stone.Color);
        Assert.Equal((byte)255, stone.A);
    }
}

public sealed class VoxelForgeSchemaHandshakeHandlerTests
{
    [Fact]
    public async Task SchemaHandshake_ReportsCompatibleWithV1()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-1", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "voxelforge@1",
                SupportedCapabilities = ["mesh_json"],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("voxelforge@1", response.SidecarSchemaVersion);
        Assert.True(response.Compatible);
        Assert.Contains("mesh_json", response.SupportedCapabilities);
    }

    [Fact]
    public async Task SchemaHandshake_ReportsIncompatibleWithUnknownVersion()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-2", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "voxelforge@99",
                SupportedCapabilities = ["mesh_json"],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Compatible);
    }
}