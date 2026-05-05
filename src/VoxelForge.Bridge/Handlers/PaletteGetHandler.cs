using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles <c>voxelforge.palette.get</c> bridge commands.
/// Returns the authoritative palette definition for the current model.
/// C#-owned response per the bridge protocol.
/// </summary>
public sealed class PaletteGetHandler : IBridgeCommandHandler<PaletteGetRequest, PaletteGetResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly PaletteSnapshotService _paletteService;

    public PaletteGetHandler(
        VoxelModelHolder modelHolder,
        PaletteSnapshotService paletteService)
    {
        _modelHolder = modelHolder;
        _paletteService = paletteService;
    }

    public ValueTask<PaletteGetResponse?> HandleAsync(
        PaletteGetRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        if (!_modelHolder.IsLoaded)
        {
            throw new BridgeHandlerException(
                "voxelforge.palette.not_loaded",
                "No model is currently loaded. Load a model before requesting a palette.",
                BridgeErrorCategories.NotFound,
                retryable: true);
        }

        var palette = _paletteService.BuildSnapshot(_modelHolder.Model.Palette);

        var entries = palette.Entries.Select(e => new PaletteEntryResponse
        {
            Index = e.Index,
            Name = e.Name,
            Color = $"#{e.R:X2}{e.G:X2}{e.B:X2}",
            A = e.A,
            Visible = e.Index != 0, // Air is invisible
        }).ToArray();

        var response = new PaletteGetResponse
        {
            PaletteId = "default",
            Entries = entries,
            EntryCount = palette.EntryCount,
        };

        return ValueTask.FromResult<PaletteGetResponse?>(response);
    }
}