using System.Diagnostics;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles <c>voxelforge.mesh.subscribe</c> bridge commands.
/// Subscribes the TS client to mesh update events for a model.
/// If requested, sends an initial full snapshot immediately.
/// <para>
/// C#-owned command per the bridge protocol. TS requests subscription;
/// C# manages subscription state and pushes update events.
/// </para>
/// </summary>
public sealed class MeshSubscribeHandler : IBridgeCommandHandler<MeshSubscribeRequest, MeshSubscribeResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly MeshSubscriptionManager _subscriptionManager;
    private readonly MeshSnapshotService _meshService;
    private readonly PaletteSnapshotService _paletteService;

    public MeshSubscribeHandler(
        VoxelModelHolder modelHolder,
        MeshSubscriptionManager subscriptionManager,
        MeshSnapshotService meshService,
        PaletteSnapshotService paletteService)
    {
        _modelHolder = modelHolder;
        _subscriptionManager = subscriptionManager;
        _meshService = meshService;
        _paletteService = paletteService;
    }

    public ValueTask<MeshSubscribeResponse?> HandleAsync(
        MeshSubscribeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        if (!_modelHolder.IsLoaded)
        {
            throw new BridgeHandlerException(
                "voxelforge.mesh.not_loaded",
                "No model is currently loaded. Load a model before subscribing to mesh updates.",
                BridgeErrorCategories.NotFound,
                retryable: true);
        }

        var modelId = string.IsNullOrEmpty(request.ModelId) ? _modelHolder.ModelId : request.ModelId;
        var chunkSize = request.ChunkSize > 0 ? request.ChunkSize : MeshRegionService.DefaultChunkSize;

        var subscription = _subscriptionManager.Subscribe(modelId, chunkSize, context.RequestId);

        MeshSnapshotResponse? initialSnapshot = null;
        if (request.SendFullSnapshotOnSubscribe)
        {
            var snapshot = _meshService.BuildSnapshot(_modelHolder.Model);

            var bounds = snapshot.Bounds is not null
                ? new BoundsDto
                {
                    MinX = snapshot.Bounds.MinX,
                    MinY = snapshot.Bounds.MinY,
                    MinZ = snapshot.Bounds.MinZ,
                    MaxX = snapshot.Bounds.MaxX,
                    MaxY = snapshot.Bounds.MaxY,
                    MaxZ = snapshot.Bounds.MaxZ,
                }
                : null;

            Dictionary<string, PaletteEntryDto>? paletteMapping = null;
            if (snapshot.PaletteIndices is not null)
            {
                paletteMapping = [];
                foreach (var (idx, mat) in _modelHolder.Model.Palette.Entries)
                {
                    paletteMapping[idx.ToString()] = new PaletteEntryDto
                    {
                        Name = mat.Name,
                        Color = $"#{mat.Color.R:X2}{mat.Color.G:X2}{mat.Color.B:X2}",
                        A = mat.Color.A,
                        Visible = true,
                    };
                }
            }

            initialSnapshot = new MeshSnapshotResponse
            {
                ModelId = modelId,
                MeshId = subscription.LastMeshId,
                Format = "json",
                VertexCount = snapshot.VertexCount,
                IndexCount = snapshot.Indices.Length,
                TriangleCount = snapshot.TriangleCount,
                Positions = snapshot.Positions,
                Normals = snapshot.Normals,
                Colors = snapshot.Colors,
                PaletteIndices = snapshot.PaletteIndices,
                Indices = snapshot.Indices,
                Bounds = bounds,
                PaletteMapping = paletteMapping,
                Metrics = new MeshSnapshotMetrics
                {
                    MeshGenerationMs = 0,
                    SerializationMs = 0,
                    TotalMs = 0,
                },
            };
        }

        return ValueTask.FromResult<MeshSubscribeResponse?>(new MeshSubscribeResponse
        {
            ModelId = modelId,
            SubscriptionId = subscription.SubscriptionId,
            ChunkSize = chunkSize,
            InitialSnapshot = initialSnapshot,
        });
    }
}