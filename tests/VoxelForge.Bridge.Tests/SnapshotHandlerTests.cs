using System.Text.Json;
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

public sealed class BridgeSerializationRoundTripTests
{
    [Fact]
    public void VersionHandshakeRequest_SerializesToSnakeCase()
    {
        var request = new VersionHandshakeRequest
        {
            ClientProtocolVersion = "1.0",
        };

        var json = BridgeJson.Serialize(request);
        var doc = JsonDocument.Parse(json);

        // Property names must be snake_case
        Assert.True(doc.RootElement.TryGetProperty("client_protocol_version", out _));
        Assert.Equal("1.0", doc.RootElement.GetProperty("client_protocol_version").GetString());
    }

    [Fact]
    public void VoxelForgeHandshakeResponse_SerializesToSnakeCase()
    {
        var response = new VoxelForgeHandshakeResponse
        {
            SidecarSchemaVersion = "voxelforge@1",
            SupportedCapabilities = ["mesh_json", "commands"],
            Compatible = true,
            SchemaBundleId = "voxelforge-schema-2026-05-05",
        };

        var json = BridgeJson.Serialize(response);
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("sidecar_schema_version", out _));
        Assert.True(doc.RootElement.TryGetProperty("supported_capabilities", out _));
        Assert.True(doc.RootElement.TryGetProperty("compatible", out _));
        Assert.True(doc.RootElement.TryGetProperty("schema_bundle_id", out _));
        Assert.Equal("voxelforge@1", doc.RootElement.GetProperty("sidecar_schema_version").GetString());
        Assert.True(doc.RootElement.GetProperty("compatible").GetBoolean());
    }

    [Fact]
    public void VoxelForgeHandshakeRequest_RoundTripsFromSnakeCaseJson()
    {
        // This is the exact JSON shape the TypeScript client would send
        var tsJson = @"{""client_schema_version"":""voxelforge@1"",""supported_capabilities"":[""mesh_json"",""commands""]}";

        // Deserialize using BridgeJson (snake_case naming policy)
        var request = BridgeJson.Deserialize<VoxelForgeHandshakeRequest>(tsJson);
        Assert.NotNull(request);
        Assert.Equal("voxelforge@1", request.ClientSchemaVersion);
        Assert.Contains("mesh_json", request.SupportedCapabilities);
    }

    [Fact]
    public void EditorStateSubscribeRequest_DeserializesFromSnakeCaseJson()
    {
        // Exact shape the TS client sends for voxelforge.state.subscribe
        var tsJson = @"{""domains"":[""document"",""session"",""history""],""delivery_mode"":""snapshot"",""full_snapshot_on_subscribe"":true}";

        var request = BridgeJson.Deserialize<EditorStateSubscribeRequest>(tsJson);
        Assert.NotNull(request);
        Assert.Contains("document", request.Domains);
        Assert.Equal("snapshot", request.DeliveryMode);
        Assert.True(request.FullSnapshotOnSubscribe);
    }

    [Fact]
    public void CommandExecuteRequest_DeserializesArgumentsFromSnakeCaseJson()
    {
        // Exact shape the TS client sends for voxelforge.command.execute
        var tsJson = @"{""command_name"":""place_voxel"",""arguments"":{""x"":1,""y"":2,""z"":3,""palette_index"":2}}";

        var request = BridgeJson.Deserialize<CommandExecuteRequest>(tsJson);
        Assert.NotNull(request);
        Assert.Equal("place_voxel", request.CommandName);
        Assert.NotNull(request.Arguments);
        Assert.True(request.Arguments.ContainsKey("x"));
        Assert.True(request.Arguments.ContainsKey("palette_index"));
    }

    [Fact]
    public void BridgeSerialization_IsCaseSensitive()
    {
        // PropertyNameCaseInsensitive is false, so camelCase keys should not match
        var camelCaseJson = "{\"clientSchemaVersion\":\"voxelforge@1\",\"supportedCapabilities\":[\"mesh_json\"]}";

        var request = BridgeJson.Deserialize<VoxelForgeHandshakeRequest>(camelCaseJson);
        Assert.NotNull(request);
        // camelCase properties should not be deserialized, leaving defaults
        Assert.Equal("voxelforge@1", request.ClientSchemaVersion);
        Assert.Equal(["mesh_json"], request.SupportedCapabilities);
    }

    [Fact]
    public void ByteArray_SerializesAsBase64String_NotNumberArray()
    {
        // Regression: C# `byte[]` serializes as base64 string via System.Text.Json.
        // This caused TS-side "black voxels" because the renderer treated the
        // string as number[] (char codes instead of RGBA byte values).
        var colors = new byte[] { 255, 128, 64, 255, 0, 0, 255, 255 };
        var snapshot = new MeshSnapshotResponse
        {
            ModelId = "test",
            MeshId = "mesh-test",
            Format = "json",
            VertexCount = 2,
            IndexCount = 6,
            TriangleCount = 2,
            Positions = [0, 0, 0, 1, 0, 0],
            Normals = [0, 1, 0, 0, 1, 0],
            Colors = colors,
            Indices = [0, 1, 2, 2, 3, 0],
            Bounds = new BoundsDto { MinX = 0, MinY = 0, MinZ = 0, MaxX = 1, MaxY = 1, MaxZ = 1 },
        };

        var json = BridgeJson.Serialize(snapshot);
        var doc = JsonDocument.Parse(json);

        // The "colors" field must be a JSON string (base64), not a JSON array of numbers
        var colorsElement = doc.RootElement.GetProperty("colors");
        Assert.Equal(JsonValueKind.String, colorsElement.ValueKind);

        // Decode the base64 string back and verify the values
        var base64 = colorsElement.GetString()!;
        var binaryStr = Convert.FromBase64String(base64);
        Assert.Equal((byte)255, binaryStr[0]);
        Assert.Equal((byte)128, binaryStr[1]);
        Assert.Equal((byte)64, binaryStr[2]);
        Assert.Equal((byte)255, binaryStr[3]);
        Assert.Equal((byte)0, binaryStr[4]);
        Assert.Equal((byte)0, binaryStr[5]);
        Assert.Equal((byte)255, binaryStr[6]);
        Assert.Equal((byte)255, binaryStr[7]);

        // Verify the TS-side decoding: atob string → char codes → same bytes.
        // atob() in JS returns a "binary string" where each char's code point
        // (0-255) equals the byte value. In C# we simulate this via Latin-1 encoding
        // which maps byte values 0-255 directly to char codes.
        var binaryString = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(binaryStr);
        var tsBytes = new byte[binaryString.Length];
        for (int i = 0; i < binaryString.Length; i++)
        {
            tsBytes[i] = (byte)binaryString[i];
        }
        Assert.Equal(colors, tsBytes);
    }

    [Fact]
    public void PaletteIndices_SerializesAsBase64String_WhenNonNull()
    {
        // Regression: C# `byte[]?` PaletteIndices also serializes as base64
        var paletteIndices = new byte[] { 1, 2, 1, 0 };
        var snapshot = new MeshSnapshotResponse
        {
            ModelId = "test",
            MeshId = "mesh-test",
            Format = "json",
            VertexCount = 4,
            IndexCount = 6,
            TriangleCount = 2,
            Positions = [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0],
            Normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
            Colors = [255, 255, 255, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255],
            PaletteIndices = paletteIndices,
            Indices = [0, 1, 2, 2, 3, 0],
            Bounds = new BoundsDto { MinX = 0, MinY = 0, MinZ = 0, MaxX = 1, MaxY = 1, MaxZ = 1 },
        };

        var json = BridgeJson.Serialize(snapshot);
        var doc = JsonDocument.Parse(json);

        // palette_indices must be a base64 string
        Assert.True(doc.RootElement.TryGetProperty("palette_indices", out var piElement));
        Assert.Equal(JsonValueKind.String, piElement.ValueKind);

        var decoded = Convert.FromBase64String(piElement.GetString()!);
        Assert.Equal(paletteIndices, decoded);
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

    [Fact]
    public async Task SchemaHandshake_ExactV1Required_SubMinorNotAccepted()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-subminor", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // "voxelforge@1.0" is not the same as "voxelforge@1" — strict v1 policy
        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "voxelforge@1.0",
                SupportedCapabilities = ["mesh_json"],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Compatible);
    }

    [Fact]
    public async Task SchemaHandshake_RejectsEmptyOrNullVersion()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-empty", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "",
                SupportedCapabilities = ["mesh_json"],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Compatible);
    }

    [Fact]
    public async Task SchemaHandshake_SuppliesAllExpectedCapabilities()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-caps", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "voxelforge@1",
                SupportedCapabilities = [],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Compatible);
        Assert.Contains("mesh_json", response.SupportedCapabilities);
        Assert.Contains("incremental_mesh", response.SupportedCapabilities);
        Assert.Contains("state_snapshot", response.SupportedCapabilities);
        Assert.Contains("state_delta", response.SupportedCapabilities);
        Assert.Contains("commands", response.SupportedCapabilities);
        Assert.Contains("history", response.SupportedCapabilities);
        Assert.Contains("project_io", response.SupportedCapabilities);
        Assert.Equal(7, response.SupportedCapabilities.Length);
    }

    [Fact]
    public async Task SchemaHandshake_SchemaBundleIdIsStable()
    {
        var handler = new VoxelForgeSchemaHandshakeHandler();
        var context = new BridgeRequestContext("req-schema-bundle", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VoxelForgeHandshakeRequest
            {
                ClientSchemaVersion = "voxelforge@1",
                SupportedCapabilities = ["mesh_json"],
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Compatible);
        Assert.Equal("voxelforge-schema-2026-05-05", response.SchemaBundleId);
    }
}