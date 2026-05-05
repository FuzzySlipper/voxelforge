using System.Diagnostics;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Core;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Pushes <c>voxelforge.mesh.update</c> event frames to subscribed TS clients
/// when the model changes. Coordinates with <see cref="MeshSubscriptionManager"/>
/// for dirty region tracking and subscription state.
/// <para>
/// This is a C#-owned service per the bridge protocol. It determines when and what
/// to push based on authoritative model state.
/// </para>
/// </summary>
public sealed class MeshChangePushService
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly MeshSubscriptionManager _subscriptionManager;
    private readonly MeshRegionService _regionService;
    private readonly IBridgeEventPublisher _eventPublisher;
    private readonly Microsoft.Extensions.Logging.ILogger<MeshChangePushService> _logger;

    public MeshChangePushService(
        VoxelModelHolder modelHolder,
        MeshSubscriptionManager subscriptionManager,
        MeshRegionService regionService,
        IBridgeEventPublisher eventPublisher,
        Microsoft.Extensions.Logging.ILogger<MeshChangePushService> logger)
    {
        _modelHolder = modelHolder;
        _subscriptionManager = subscriptionManager;
        _regionService = regionService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Push a mesh update for all dirty regions of the current model.
    /// Called when the model has changed (e.g., after a voxel edit).
    /// Does nothing if there are no active subscriptions for the model.
    /// </summary>
    public async ValueTask PushMeshUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!_modelHolder.IsLoaded)
        {
            return;
        }

        var modelId = _modelHolder.ModelId;
        var subscriptions = _subscriptionManager.GetSubscriptions(modelId);
        if (subscriptions.Count == 0)
        {
            // No subscribers — skip push.
            return;
        }

        // Consume dirty regions
        var dirtyRegions = _subscriptionManager.ConsumeDirtyRegions(modelId);

        // If no dirty regions recorded, do a full replace of the whole model
        // (this handles cases where the event didn't carry bounds info)
        bool isFullReplace = dirtyRegions is null || dirtyRegions.Count == 0;

        var totalStopwatch = Stopwatch.StartNew();

        // Determine chunk size from first subscription (all should have same chunk size for now)
        int chunkSize = subscriptions[0].ChunkSize;

        // Build incremental update
        var baseMeshId = _subscriptionManager.GetLastMeshId(modelId) ?? $"mesh-{modelId}-initial";
        MeshIncrementalUpdate update;

        if (isFullReplace)
        {
            // Full replace — include all occupied regions
            var allRegions = MeshRegionService.GetOccupiedRegions(_modelHolder.Model, chunkSize);
            update = _regionService.BuildIncrementalUpdate(
                _modelHolder.Model,
                allRegions,
                modelId,
                baseMeshId,
                chunkSize);
        }
        else
        {
            update = _regionService.BuildIncrementalUpdate(
                _modelHolder.Model,
                dirtyRegions!,
                modelId,
                baseMeshId,
                chunkSize);
        }

        totalStopwatch.Stop();
        update.Metrics = new MeshUpdateMetrics
        {
            RegionCount = update.ChangedRegions.Count,
            BuildMs = totalStopwatch.ElapsedMilliseconds,
            SerializeMs = 0,
        };

        // Generate new mesh ID for this update
        var newMeshId = _subscriptionManager.GenerateMeshId(modelId);

        // Build event payload
        var payload = MapToUpdateEventPayload(update, newMeshId);

        // Publish event frame
        var frame = new BridgeEventFrame
        {
            EventId = $"evt-mesh-{Guid.NewGuid():N}",
            Sequence = _subscriptionManager.NextSequence(),
            Event = "voxelforge.mesh.update",
            Payload = Den.Bridge.Protocol.BridgeJson.ToElement(payload),
        };

        await _eventPublisher.PublishAsync(frame, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Pushed mesh update: model={ModelId}, base={BaseMeshId}, type={UpdateType}, regions={RegionCount}, build={BuildMs}ms",
            modelId, baseMeshId, update.UpdateType, update.ChangedRegions.Count, totalStopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Push a full model replacement as a mesh update event.
    /// Used for initial sync or after model load.
    /// </summary>
    public async ValueTask PushFullReplaceAsync(CancellationToken cancellationToken = default)
    {
        if (!_modelHolder.IsLoaded)
        {
            return;
        }

        var modelId = _modelHolder.ModelId;
        var subscriptions = _subscriptionManager.GetSubscriptions(modelId);
        if (subscriptions.Count == 0)
        {
            return;
        }

        // Mark all regions dirty and push
        _subscriptionManager.RecordFullDirty(modelId, _modelHolder.Model);
        await PushMeshUpdateAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MeshUpdateEventPayload MapToUpdateEventPayload(MeshIncrementalUpdate update, string newMeshId)
    {
        var regions = new MeshRegionUpdateDto[update.ChangedRegions.Count];
        for (int i = 0; i < update.ChangedRegions.Count; i++)
        {
            var r = update.ChangedRegions[i];
            regions[i] = new MeshRegionUpdateDto
            {
                RegionId = r.RegionId,
                UpdateKind = r.UpdateKind == App.Services.MeshRegionUpdateKind.Incremental ? "incremental" : "full_replace",
                Bounds = new RegionBoundsDto
                {
                    MinX = r.Bounds.MinX,
                    MinY = r.Bounds.MinY,
                    MinZ = r.Bounds.MinZ,
                    MaxX = r.Bounds.MaxX,
                    MaxY = r.Bounds.MaxY,
                    MaxZ = r.Bounds.MaxZ,
                },
                VertexOffset = r.VertexOffset,
                VertexCount = r.VertexCount,
                IndexOffset = r.IndexOffset,
                IndexCount = r.IndexCount,
                Positions = r.Positions,
                Normals = r.Normals,
                Colors = r.Colors,
                PaletteIndices = r.PaletteIndices,
                Indices = r.Indices,
            };
        }

        var sequence = 0L; // Caller should set sequence from subscription manager
        var payload = new MeshUpdateEventPayload
        {
            ModelId = update.ModelId,
            BaseMeshId = update.BaseMeshId,
            Sequence = sequence,
            UpdateType = update.UpdateType == App.Services.MeshUpdateType.Incremental ? "incremental" : "full_replace",
            ChangedRegions = regions,
            PayloadFormat = update.PayloadFormat,
            FullVertexCount = update.FullVertexCount,
            FullIndexCount = update.FullIndexCount,
            Metrics = update.Metrics is not null
                ? new MeshUpdateMetricsDto
                {
                    RegionCount = update.Metrics.RegionCount,
                    BuildMs = update.Metrics.BuildMs,
                    SerializeMs = update.Metrics.SerializeMs,
                }
                : null,
        };

        return payload;
    }
}