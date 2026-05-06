using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Core;

namespace VoxelForge.Bridge.Tests;

public sealed class EditorUiStateHandlerTests
{
    [Fact]
    public async Task StateRequestFull_ReturnsAuthoritativeEditorState()
    {
        var (stateService, _) = CreateStateService();
        var handler = new EditorStateRequestFullHandler(stateService);
        var context = new BridgeRequestContext("req-state", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(new EditorStateRequestFullRequest(), context, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("default-cube", response.Snapshot.ModelId);
        Assert.Equal("place", response.Snapshot.ActiveTool);
        Assert.Equal((byte)1, response.Snapshot.ActivePaletteIndex);
        Assert.True(response.Snapshot.VoxelCount > 0);
        Assert.True(response.Snapshot.PaletteEntryCount >= 2);
        Assert.False(response.Snapshot.CanUndo);
    }

    [Fact]
    public async Task CommandExecute_SetActiveTool_MutatesCSharpSessionStateAndPublishesStateEvent()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler(out var publisher);
        var context = new BridgeRequestContext("req-command", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "set_active_tool",
                Arguments = new Dictionary<string, object?> { ["tool"] = "paint" },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(response.MeshChanged);
        Assert.Equal("paint", response.State.ActiveTool);
        Assert.Equal("paint", holder.Session.ActiveTool.ToString().ToLowerInvariant());
        Assert.Contains(publisher.PublishedFrames, f => f.Event == "voxelforge.state.delta");
        Assert.Contains(publisher.PublishedFrames, f => f.Event == "voxelforge.diagnostics.editing_latency");
    }

    [Fact]
    public async Task CommandExecute_SetActivePalette_RejectsUnknownPaletteIndex()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-command-invalid", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "set_active_palette",
                    Arguments = new Dictionary<string, object?> { ["palette_index"] = 99 },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.invalid_palette_index", ex.Code);
        Assert.Equal(BridgeErrorCategories.Validation, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_PlaceVoxel_UpdatesAuthoritativeModel()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-place", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "place_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 10,
                    ["y"] = 10,
                    ["z"] = 10,
                    ["palette_index"] = 1,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.MeshChanged);
        Assert.Equal(startingCount + 1, holder.Model.GetVoxelCount());
        Assert.NotNull(holder.Model.GetVoxel(new Point3(10, 10, 10)));
        Assert.True(holder.UndoStack.CanUndo);
    }

    [Fact]
    public async Task CommandExecute_RemoveVoxel_UpdatesAuthoritativeModel()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-remove", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        // Remove voxel at origin (0,0,0) which exists in the default cube
        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "remove_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 0,
                    ["y"] = 0,
                    ["z"] = 0,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.MeshChanged);
        Assert.Equal(startingCount - 1, holder.Model.GetVoxelCount());
        Assert.Null(holder.Model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public async Task CommandExecute_PaintVoxel_UpdatesAuthoritativeModel()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-paint", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "paint_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 0,
                    ["y"] = 0,
                    ["z"] = 0,
                    ["palette_index"] = 2,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.MeshChanged);
        Assert.Equal((byte)2, holder.Model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public async Task CommandExecute_SelectVoxel_UpdatesSessionState()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-select", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "select_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 0,
                    ["y"] = 0,
                    ["z"] = 0,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(response.MeshChanged);
        Assert.Single(holder.Session.SelectedVoxels);
        Assert.Contains(new Point3(0, 0, 0), holder.Session.SelectedVoxels);
        Assert.Equal(1, response.State.SelectedVoxelCount);
    }

    [Fact]
    public async Task CommandExecute_ClearSelection_UpdatesSessionState()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-clear-sel", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Add a selection first
        holder.Session.SelectedVoxels.Add(new Point3(0, 0, 0));
        Assert.Single(holder.Session.SelectedVoxels);

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "clear_selection",
                Arguments = new Dictionary<string, object?>(),
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.False(response.MeshChanged);
        Assert.Empty(holder.Session.SelectedVoxels);
        Assert.Equal(0, response.State.SelectedVoxelCount);
    }

    [Fact]
    public async Task CommandExecute_FillRegion_UpdatesAuthoritativeModel()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-fill", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "fill_region",
                Arguments = new Dictionary<string, object?>
                {
                    ["min_x"] = 5,
                    ["min_y"] = 5,
                    ["min_z"] = 5,
                    ["max_x"] = 7,
                    ["max_y"] = 7,
                    ["max_z"] = 7,
                    ["palette_index"] = 1,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.MeshChanged);
        // 3x3x3 = 27 new voxels
        Assert.Equal(startingCount + 27, holder.Model.GetVoxelCount());
    }

    [Fact]
    public async Task CommandExecute_EditingCommands_SupportUndo()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-undotest", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        // Place a voxel
        await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "place_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 20,
                    ["y"] = 20,
                    ["z"] = 20,
                    ["palette_index"] = 1,
                },
            },
            context,
            CancellationToken.None);

        Assert.True(holder.UndoStack.CanUndo);
        Assert.Equal(startingCount + 1, holder.Model.GetVoxelCount());

        // Undo via UndoStack
        holder.UndoStack.Undo();
        Assert.Equal(startingCount, holder.Model.GetVoxelCount());
        Assert.Null(holder.Model.GetVoxel(new Point3(20, 20, 20)));

        // Redo via UndoStack
        holder.UndoStack.Redo();
        Assert.Equal(startingCount + 1, holder.Model.GetVoxelCount());
        Assert.NotNull(holder.Model.GetVoxel(new Point3(20, 20, 20)));
    }

    [Fact]
    public async Task CommandExecute_RemoveNonexistentVoxel_DoesNotThrow()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-remove-nonex", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        // Remove a voxel at a position that does not exist
        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "remove_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 999,
                    ["y"] = 999,
                    ["z"] = 999,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        // Removing a non-existent voxel should be a no-op (count unchanged)
        Assert.Equal(startingCount, holder.Model.GetVoxelCount());
    }

    [Fact]
    public async Task CommandExecute_PlaceAtOccupiedPosition_OverwritesExisting()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-place-occupied", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Position (0,0,0) is occupied in the default cube with palette index 1
        Assert.NotNull(holder.Model.GetVoxel(new Point3(0, 0, 0)));

        var startingCount = holder.Model.GetVoxelCount();

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "place_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 0,
                    ["y"] = 0,
                    ["z"] = 0,
                    ["palette_index"] = 2,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.True(response.MeshChanged);
        // Voxel count should remain the same (overwrite, not add)
        Assert.Equal(startingCount, holder.Model.GetVoxelCount());
        // Palette index should have changed
        Assert.Equal((byte)2, holder.Model.GetVoxel(new Point3(0, 0, 0)));
    }

    [Fact]
    public async Task CommandExecute_FillRegionWithMinGreaterThanMax_IsNoOp()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-fill-reversed", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var startingCount = holder.Model.GetVoxelCount();

        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "fill_region",
                Arguments = new Dictionary<string, object?>
                {
                    ["min_x"] = 10,
                    ["min_y"] = 10,
                    ["min_z"] = 10,
                    ["max_x"] = 5,
                    ["max_y"] = 5,
                    ["max_z"] = 5,
                    ["palette_index"] = 1,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);
        // With min > max, the loop body never executes, so no voxels change
        Assert.Equal(startingCount, holder.Model.GetVoxelCount());
    }

    [Fact]
    public async Task CommandExecute_InvalidPaletteIndexOutOfRange_ThrowsValidationError()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-invalid-pal", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "place_voxel",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["x"] = 5,
                        ["y"] = 5,
                        ["z"] = 5,
                        ["palette_index"] = -1,
                    },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.invalid_palette_index", ex.Code);
        Assert.Equal(BridgeErrorCategories.Validation, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_MalformedStringArgument_ThrowsValidationError()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-malformed-str", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // The tool argument should be a string; passing a number should throw
        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "set_active_tool",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["tool"] = new System.Text.Json.JsonElement()  // Non-string JsonElement
                    },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.invalid_argument", ex.Code);
        Assert.Equal(BridgeErrorCategories.Validation, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_MalformedIntArgument_ThrowsValidationError()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-malformed-int", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // The x argument should be an integer; passing a string should throw
        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "place_voxel",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["x"] = "not-a-number",
                        ["y"] = 0,
                        ["z"] = 0,
                        ["palette_index"] = 1,
                    },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.invalid_argument", ex.Code);
    }

    [Fact]
    public async Task CommandExecute_MalformedIntArgument_NonNumericJsonElement_ThrowsValidationError()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-malformed-json", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Simulate a JsonElement that is not a Number (e.g., True/False/Object)
        var jsonElement = System.Text.Json.JsonDocument.Parse("true").RootElement;

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "place_voxel",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["x"] = jsonElement,
                        ["y"] = 0,
                        ["z"] = 0,
                        ["palette_index"] = 1,
                    },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.invalid_argument", ex.Code);
        Assert.Equal(BridgeErrorCategories.Validation, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_UnsupportedCommand_ThrowsBridgeHandlerException()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-unsupported", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "nonexistent_command",
                    Arguments = new Dictionary<string, object?>(),
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.unsupported", ex.Code);
        Assert.Equal(BridgeErrorCategories.UnsupportedCapability, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_MissingRequiredArgument_ThrowsValidationError()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-missing-arg", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await commandHandler.HandleAsync(
                new CommandExecuteRequest
                {
                    CommandName = "place_voxel",
                    Arguments = new Dictionary<string, object?>
                    {
                        ["x"] = 5,
                        ["y"] = 5,
                        // Missing "z" and "palette_index"
                    },
                },
                context,
                CancellationToken.None);
        });

        Assert.Equal("voxelforge.command.missing_argument", ex.Code);
        Assert.Equal(BridgeErrorCategories.Validation, ex.Category);
    }

    [Fact]
    public async Task CommandExecute_AddToSelection_Accumulates()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler();
        var context = new BridgeRequestContext("req-addsel", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Add first selection
        var response1 = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "add_to_selection",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 0,
                    ["y"] = 0,
                    ["z"] = 0,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response1);
        Assert.True(response1.Success);
        Assert.False(response1.MeshChanged);
        Assert.Equal(1, response1.State.SelectedVoxelCount);

        // Add second selection (accumulates)
        var response2 = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "add_to_selection",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 1,
                    ["y"] = 1,
                    ["z"] = 1,
                },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response2);
        Assert.Equal(2, response2.State.SelectedVoxelCount);
        Assert.Equal(2, holder.Session.SelectedVoxels.Count);
        Assert.Contains(new Point3(0, 0, 0), holder.Session.SelectedVoxels);
        Assert.Contains(new Point3(1, 1, 1), holder.Session.SelectedVoxels);
    }

    [Fact]
    public async Task CommandExecute_PlaceVoxel_RecordsDirtyRegionsForMeshPush()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler(out var publisher, out var meshSubManager);
        var context = new BridgeRequestContext("req-mesh-event", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Subscribe to mesh updates so the push service sends events
        meshSubManager.Subscribe(holder.ModelId, 16, "req-sub");

        await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "place_voxel",
                Arguments = new Dictionary<string, object?>
                {
                    ["x"] = 10,
                    ["y"] = 10,
                    ["z"] = 10,
                    ["palette_index"] = 1,
                },
            },
            context,
            CancellationToken.None);

        // Should have published a mesh update event
        var meshUpdateFrames = publisher.PublishedFrames
            .Where(f => f.Event == "voxelforge.mesh.update")
            .ToList();

        Assert.NotEmpty(meshUpdateFrames);
        var meshFrame = meshUpdateFrames[0];
        Assert.Equal("voxelforge.mesh.update", meshFrame.Event);
        Assert.True(meshFrame.Sequence > 0);
    }

    [Fact]
    public async Task CommandExecute_SetActiveTool_DoesNotPublishMeshUpdate()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler(out var publisher);
        var context = new BridgeRequestContext("req-no-mesh", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "set_active_tool",
                Arguments = new Dictionary<string, object?> { ["tool"] = "paint" },
            },
            context,
            CancellationToken.None);

        // No mesh update events should be published for non-mesh commands
        var meshUpdateFrames = publisher.PublishedFrames
            .Where(f => f.Event == "voxelforge.mesh.update")
            .ToList();

        Assert.Empty(meshUpdateFrames);
    }

    [Fact]
    public async Task CommandExecute_LatencyEventIsEmittedUnconditionally()
    {
        var (stateService, holder, commandHandler) = CreateCommandHandler(out var publisher);
        var context = new BridgeRequestContext("req-latency", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        // Execute a fast command (set_active_tool) that completes well under 100ms
        var response = await commandHandler.HandleAsync(
            new CommandExecuteRequest
            {
                CommandName = "set_active_tool",
                Arguments = new Dictionary<string, object?> { ["tool"] = "select" },
            },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify a latency diagnostic event was published
        var latencyFrames = publisher.PublishedFrames
            .Where(f => f.Event == "voxelforge.diagnostics.editing_latency")
            .ToList();

        Assert.NotEmpty(latencyFrames);
        var latencyPayload = BridgeJson.Deserialize<EditingLatencyEventPayload>(latencyFrames[0].Payload.GetRawText());
        Assert.NotNull(latencyPayload);
        Assert.Equal("set_active_tool", latencyPayload.CommandName);
        Assert.True(latencyPayload.TotalMs >= 0);
        Assert.True(latencyPayload.CSharpProcessingMs >= 0);
        Assert.True(latencyPayload.MeshUpdateMs >= 0);
    }

    private static (EditorUiStateBridgeService stateService, VoxelModelHolder holder, CommandExecuteHandler handler) CreateCommandHandler()
    {
        return CreateCommandHandler(out _);
    }

    private static (EditorUiStateBridgeService stateService, VoxelModelHolder holder, CommandExecuteHandler handler) CreateCommandHandler(out FakeBridgeEventPublisher publisher)
    {
        return CreateCommandHandler(out publisher, out _);
    }

    private static (EditorUiStateBridgeService stateService, VoxelModelHolder holder, CommandExecuteHandler handler) CreateCommandHandler(out FakeBridgeEventPublisher publisher, out MeshSubscriptionManager meshSubManager)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(NullLogger<VoxelModelHolder>.Instance, loggerFactory);
        holder.LoadDefaultCube();
        var paletteService = new PaletteSnapshotService();
        publisher = new FakeBridgeEventPublisher();
        var stateService = new EditorUiStateBridgeService(holder, paletteService, publisher);

        var voxelEditing = new VoxelEditingService();
        var eventPublisher = new NoopAppEventPublisher();
        meshSubManager = new MeshSubscriptionManager();
        var meshPushService = new MeshChangePushService(
            holder,
            meshSubManager,
            new MeshRegionService(new VoxelForge.Core.Meshing.GreedyMesher()),
            publisher,
            NullLogger<MeshChangePushService>.Instance);

        var handler = new CommandExecuteHandler(
            holder,
            stateService,
            voxelEditing,
            eventPublisher,
            meshSubManager,
            meshPushService,
            NullLogger<CommandExecuteHandler>.Instance,
            publisher);

        return (stateService, holder, handler);
    }

    private static (EditorUiStateBridgeService stateService, VoxelModelHolder holder) CreateStateService()
    {
        return CreateStateService(out _);
    }

    private static (EditorUiStateBridgeService stateService, VoxelModelHolder holder) CreateStateService(out FakeBridgeEventPublisher publisher)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var holder = new VoxelModelHolder(NullLogger<VoxelModelHolder>.Instance, loggerFactory);
        holder.LoadDefaultCube();
        var paletteService = new PaletteSnapshotService();
        publisher = new FakeBridgeEventPublisher();
        var stateService = new EditorUiStateBridgeService(holder, paletteService, publisher);
        return (stateService, holder);
    }
}

/// <summary>
/// Minimal IEventPublisher no-op for tests that don't need event assertions.
/// </summary>
internal sealed class NoopAppEventPublisher : IEventPublisher
{
    public void Publish<TEvent>(TEvent applicationEvent) where TEvent : IApplicationEvent { }
    public void Publish(IApplicationEvent applicationEvent) { }
    public void PublishAll(IReadOnlyList<IApplicationEvent> applicationEvents) { }
}
