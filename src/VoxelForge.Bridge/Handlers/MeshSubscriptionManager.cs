using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Tracks mesh subscriptions and computes dirty regions from model change events.
/// When a model change event is published, this handler determines which regions
/// are affected and records them for the next mesh update push.
/// <para>
/// C#-owned logic per the bridge protocol. TS subscribes/unsubscribes;
/// C# pushes update events when the model changes.
/// </para>
/// </summary>
public sealed class MeshSubscriptionManager : IEventHandler<VoxelModelChangedEvent>
{
    private readonly object _lock = new();
    private readonly Dictionary<string, MeshSubscription> _subscriptions = [];

    /// <summary>
    /// Incrementing sequence counter for mesh update events.
    /// </summary>
    private long _globalSequence;

    /// <summary>
    /// Tracks dirty regions per model since last push.
    /// Key is model ID; value is the set of dirty region identifiers.
    /// </summary>
    private readonly Dictionary<string, HashSet<App.Services.MeshRegionCoord>> _dirtyRegions = [];

    /// <summary>
    /// Tracks the last mesh snapshot ID per model so that incremental
    /// updates can reference the correct base.
    /// </summary>
    private readonly Dictionary<string, string> _lastMeshIds = [];

    /// <summary>
    /// Subscribe to mesh updates for a model.
    /// Returns the subscription record with a unique ID.
    /// </summary>
    public MeshSubscription Subscribe(string modelId, int chunkSize, string requestId)
    {
        var subscriptionId = $"sub-{Guid.NewGuid():N}";
        var meshId = $"mesh-{modelId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

        var subscription = new MeshSubscription
        {
            SubscriptionId = subscriptionId,
            ModelId = modelId,
            ChunkSize = chunkSize,
            RequestId = requestId,
            CreatedAt = DateTimeOffset.UtcNow,
            LastMeshId = meshId,
            Sequence = 0,
        };

        lock (_lock)
        {
            _subscriptions[subscriptionId] = subscription;
            _lastMeshIds[modelId] = meshId;
        }

        return subscription;
    }

    /// <summary>
    /// Unsubscribe from mesh updates.
    /// </summary>
    public void Unsubscribe(string subscriptionId)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscriptionId);
        }
    }

    /// <summary>
    /// Get all active subscriptions for a model.
    /// </summary>
    public IReadOnlyList<MeshSubscription> GetSubscriptions(string modelId)
    {
        lock (_lock)
        {
            return _subscriptions.Values
                .Where(s => s.ModelId == modelId)
                .ToList();
        }
    }

    /// <summary>
    /// Record dirty regions from a model change event.
    /// Called by the event handler when a VoxelModelChangedEvent is published.
    /// </summary>
    public void RecordDirtyRegions(string modelId, IEnumerable<App.Services.MeshRegionCoord> regions)
    {
        lock (_lock)
        {
            if (!_dirtyRegions.TryGetValue(modelId, out var existing))
            {
                existing = [];
                _dirtyRegions[modelId] = existing;
            }

            foreach (var region in regions)
            {
                existing.Add(region);
            }
        }
    }

    /// <summary>
    /// Record a full-model dirty state (e.g., project load).
    /// Marks all currently occupied regions as dirty.
    /// </summary>
    public void RecordFullDirty(string modelId, VoxelModel model)
    {
        var allRegions = App.Services.MeshRegionService.GetOccupiedRegions(model);
        RecordDirtyRegions(modelId, allRegions);
    }

    /// <summary>
    /// Consume and return the currently dirty regions for a model.
    /// Clears the dirty state after consumption.
    /// Returns null if there are no dirty regions.
    /// </summary>
    public HashSet<App.Services.MeshRegionCoord>? ConsumeDirtyRegions(string modelId)
    {
        lock (_lock)
        {
            if (!_dirtyRegions.TryGetValue(modelId, out var regions) || regions.Count == 0)
            {
                return null;
            }

            _dirtyRegions.Remove(modelId);
            return regions;
        }
    }

    /// <summary>
    /// Generate a new mesh ID for a model update and update the last-known mesh ID.
    /// </summary>
    public string GenerateMeshId(string modelId)
    {
        var meshId = $"mesh-{modelId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        lock (_lock)
        {
            _lastMeshIds[modelId] = meshId;
        }

        return meshId;
    }

    /// <summary>
    /// Get the last-known mesh ID for a model (base for incremental updates).
    /// </summary>
    public string? GetLastMeshId(string modelId)
    {
        lock (_lock)
        {
            return _lastMeshIds.TryGetValue(modelId, out var id) ? id : null;
        }
    }

    /// <summary>
    /// Get the next global sequence number for an event.
    /// </summary>
    public long NextSequence()
    {
        return Interlocked.Increment(ref _globalSequence);
    }

    /// <summary>
    /// Get the number of active subscriptions for a model.
    /// </summary>
    public int GetSubscriptionCount(string modelId)
    {
        lock (_lock)
        {
            return _subscriptions.Values.Count(s => s.ModelId == modelId);
        }
    }

    // IEventHandler<VoxelModelChangedEvent> implementation

    void IEventHandler<VoxelModelChangedEvent>.Handle(VoxelModelChangedEvent applicationEvent)
    {
        // Intentionally a no-op. Dirty region recording for mesh updates is done
        // synchronously by the bridge command handlers (CommandExecuteHandler,
        // HistoryUndoHandler, HistoryRedoHandler, ProjectLoadHandler), which call
        // RecordFullDirty() directly after mutating the model. The event-based path
        // is reserved for future enhancement when VoxelModelChangedEvent carries
        // exact changed-bounds metadata, enabling finer-grained dirty region tracking.
        //
        // Until then, marking all occupied regions dirty via the command handler's
        // RecordFullDirty() call is safe and produces correct mesh updates at the
        // cost of re-meshing regions that may not have changed.
    }
}

/// <summary>
/// Tracks a single mesh subscription.
/// </summary>
public sealed class MeshSubscription
{
    public required string SubscriptionId { get; init; }
    public required string ModelId { get; init; }
    public required int ChunkSize { get; init; }
    public required string RequestId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string LastMeshId { get; set; }
    public required long Sequence { get; set; }
}