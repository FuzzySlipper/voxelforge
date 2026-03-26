using Microsoft.Extensions.Logging;

namespace VoxelForge.Core;

/// <summary>
/// Dual-indexed runtime lookup for voxel region labels.
/// Derived from RegionDefs on load; updated incrementally on edit.
/// Never serialized — always rebuilt from the authoritative RegionDef set.
/// </summary>
public sealed class LabelIndex
{
    private readonly Dictionary<RegionId, RegionDef> _regions = [];
    private readonly Dictionary<Point3, RegionId> _byVoxel = [];
    private readonly ILogger<LabelIndex> _logger;

    public IReadOnlyDictionary<RegionId, RegionDef> Regions => _regions;

    public LabelIndex(ILogger<LabelIndex> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register or replace a region definition. Does not touch voxel assignments.
    /// </summary>
    public void AddOrUpdateRegion(RegionDef def)
    {
        _regions[def.Id] = def;
        _logger.LogTrace("AddOrUpdateRegion({Id}, {Name})", def.Id, def.Name);
    }

    /// <summary>
    /// Remove a region definition and unindex all its voxels.
    /// </summary>
    public void RemoveRegion(RegionId id)
    {
        if (!_regions.TryGetValue(id, out var def)) return;

        foreach (var pos in def.Voxels)
            _byVoxel.Remove(pos);

        _regions.Remove(id);
        _logger.LogTrace("RemoveRegion({Id})", id);
    }

    /// <summary>
    /// Assign voxels to a region. Automatically removes each voxel from any previous region.
    /// </summary>
    public void AssignRegion(RegionId id, IEnumerable<Point3> voxels)
    {
        if (!_regions.TryGetValue(id, out var targetDef))
            throw new InvalidOperationException($"Region '{id}' does not exist. Call AddOrUpdateRegion first.");

        foreach (var pos in voxels)
        {
            // Remove from old region if assigned
            if (_byVoxel.TryGetValue(pos, out var oldId) && _regions.TryGetValue(oldId, out var oldDef))
                oldDef.Voxels.Remove(pos);

            _byVoxel[pos] = id;
            targetDef.Voxels.Add(pos);
        }

        _logger.LogTrace("AssignRegion({Id}, count={Count})", id, targetDef.Voxels.Count);
    }

    /// <summary>
    /// Remove a voxel from whatever region it belongs to.
    /// </summary>
    public void RemoveFromRegion(Point3 pos)
    {
        if (!_byVoxel.TryGetValue(pos, out var regionId)) return;
        if (_regions.TryGetValue(regionId, out var def))
            def.Voxels.Remove(pos);
        _byVoxel.Remove(pos);
    }

    /// <summary>
    /// Get the region a voxel belongs to, or null if unlabeled.
    /// </summary>
    public RegionId? GetRegion(Point3 pos)
    {
        return _byVoxel.TryGetValue(pos, out var id) ? id : null;
    }

    /// <summary>
    /// Get all voxels in a region.
    /// </summary>
    public IReadOnlySet<Point3> GetVoxelsInRegion(RegionId id)
    {
        return _regions.TryGetValue(id, out var def) ? def.Voxels : new HashSet<Point3>();
    }

    /// <summary>
    /// Walk the parent chain from root to the given region (inclusive).
    /// Returns [root, ..., parent, id].
    /// </summary>
    public IReadOnlyList<RegionId> GetAncestors(RegionId id)
    {
        var chain = new List<RegionId>();
        var visited = new HashSet<RegionId>();
        var current = id;

        // Walk up the parent chain, collecting ancestors
        while (_regions.TryGetValue(current, out var def))
        {
            chain.Add(current);
            if (!def.ParentId.HasValue || !visited.Add(current))
                break;
            current = def.ParentId.Value;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Get all child and grandchild regions of the given region.
    /// </summary>
    public IReadOnlyList<RegionId> GetDescendants(RegionId id)
    {
        var result = new List<RegionId>();
        var queue = new Queue<RegionId>();
        queue.Enqueue(id);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var (childId, childDef) in _regions)
            {
                if (childDef.ParentId.HasValue && childDef.ParentId.Value == current)
                {
                    result.Add(childId);
                    queue.Enqueue(childId);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Rebuild both indexes from a set of RegionDefs (e.g. after loading from disk).
    /// </summary>
    public void Rebuild(IEnumerable<RegionDef> defs)
    {
        _regions.Clear();
        _byVoxel.Clear();

        foreach (var def in defs)
        {
            _regions[def.Id] = def;
            foreach (var pos in def.Voxels)
                _byVoxel[pos] = def.Id;
        }

        _logger.LogTrace("Rebuild complete, {RegionCount} regions, {VoxelCount} labeled voxels",
            _regions.Count, _byVoxel.Count);
    }
}
