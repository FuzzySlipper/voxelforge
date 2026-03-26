using Microsoft.Extensions.Logging;

namespace VoxelForge.Core;

/// <summary>
/// Sparse voxel grid — the authoring source of truth.
/// Stores non-air voxels as a dictionary keyed by position.
/// </summary>
public sealed class VoxelModel
{
    private readonly Dictionary<Point3, byte> _voxels = [];
    private readonly ILogger<VoxelModel> _logger;

    public Palette Palette { get; } = new();

    /// <summary>
    /// Advisory grid resolution hint (e.g. 32, 64). Not enforced.
    /// </summary>
    public int GridHint { get; set; } = 32;

    public IReadOnlyDictionary<Point3, byte> Voxels => _voxels;

    public VoxelModel(ILogger<VoxelModel> logger)
    {
        _logger = logger;
    }

    public void SetVoxel(Point3 pos, byte paletteIndex)
    {
        _voxels[pos] = paletteIndex;
        _logger.LogTrace("SetVoxel({Pos}, {Index})", pos, paletteIndex);
    }

    public void RemoveVoxel(Point3 pos)
    {
        if (_voxels.Remove(pos))
            _logger.LogTrace("RemoveVoxel({Pos})", pos);
    }

    public byte? GetVoxel(Point3 pos)
    {
        return _voxels.TryGetValue(pos, out var v) ? v : null;
    }

    public void FillRegion(Point3 min, Point3 max, byte paletteIndex)
    {
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
            _voxels[new Point3(x, y, z)] = paletteIndex;

        _logger.LogTrace("FillRegion({Min} -> {Max}, {Index})", min, max, paletteIndex);
    }

    public int GetVoxelCount() => _voxels.Count;

    /// <summary>
    /// Returns the axis-aligned bounding box of all non-air voxels, or null if empty.
    /// </summary>
    public (Point3 Min, Point3 Max)? GetBounds()
    {
        if (_voxels.Count == 0) return null;

        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;

        foreach (var pos in _voxels.Keys)
        {
            if (pos.X < minX) minX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Z < minZ) minZ = pos.Z;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y > maxY) maxY = pos.Y;
            if (pos.Z > maxZ) maxZ = pos.Z;
        }

        return (new Point3(minX, minY, minZ), new Point3(maxX, maxY, maxZ));
    }
}
