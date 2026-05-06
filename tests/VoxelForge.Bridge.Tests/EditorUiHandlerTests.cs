using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;

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
        var (stateService, holder) = CreateStateService(out var publisher);
        var handler = new CommandExecuteHandler(holder, stateService);
        var context = new BridgeRequestContext("req-command", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
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
        Assert.Single(publisher.PublishedFrames);
        Assert.Equal("voxelforge.state.delta", publisher.PublishedFrames[0].Event);
    }

    [Fact]
    public async Task CommandExecute_SetActivePalette_RejectsUnknownPaletteIndex()
    {
        var (stateService, holder) = CreateStateService();
        var handler = new CommandExecuteHandler(holder, stateService);
        var context = new BridgeRequestContext("req-command-invalid", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var ex = await Assert.ThrowsAsync<BridgeHandlerException>(async () =>
        {
            await handler.HandleAsync(
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
