using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Services;

/// <summary>
/// Identifies dirty regions within a <see cref="VoxelModel"/> and builds incremental
/// mesh updates keyed by stable region identifiers. This service is renderer-neutral:
/// it produces structured data that the bridge layer can serialize for TS consumption.
/// <para>
/// C# owns dirty region detection; TS receives the results and replaces buffers.
/// </para>
/// </summary>
public sealed class MeshRegionService
{
    private readonly IVoxelMesher _mesher;

    /// <summary>
    /// Default chunk size for dividing the model into stable regions.
    /// Each region covers a <see cref="RegionBounds"/> of this size per axis.
    /// </summary>
    public const int DefaultChunkSize = 16;

    public MeshRegionService(IVoxelMesher mesher)
    {
        ArgumentNullException.ThrowIfNull(mesher);
        _mesher = mesher;
    }

    /// <summary>
    /// Compute the set of region identifiers that cover all voxels in the model,
    /// using the specified chunk size. Returns one region per chunk that contains
    /// at least one voxel.
    /// </summary>
    public static HashSet<MeshRegionCoord> GetOccupiedRegions(VoxelModel model, int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        var regions = new HashSet<MeshRegionCoord>();

        foreach (var kvp in model.Voxels)
        {
            var regionCoord = MeshRegionCoord.FromPoint(kvp.Key, chunkSize);
            regions.Add(regionCoord);
        }

        return regions;
    }

    /// <summary>
    /// Compute the set of region identifiers affected by a bounded change.
    /// This is the primary method for determining which regions need mesh updates
    /// after a voxel edit.
    /// </summary>
    public static HashSet<MeshRegionCoord> GetAffectedRegions(
        Point3 changeMin,
        Point3 changeMax,
        int chunkSize = DefaultChunkSize)
    {
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        var regions = new HashSet<MeshRegionCoord>();

        int minRX = FloorDiv(changeMin.X, chunkSize);
        int minRY = FloorDiv(changeMin.Y, chunkSize);
        int minRZ = FloorDiv(changeMin.Z, chunkSize);
        int maxRX = FloorDiv(changeMax.X, chunkSize);
        int maxRY = FloorDiv(changeMax.Y, chunkSize);
        int maxRZ = FloorDiv(changeMax.Z, chunkSize);

        for (int rx = minRX; rx <= maxRX; rx++)
        for (int ry = minRY; ry <= maxRY; ry++)
        for (int rz = minRZ; rz <= maxRZ; rz++)
            regions.Add(new MeshRegionCoord(rx, ry, rz));

        return regions;
    }

    /// <summary>
    /// Build an incremental mesh update from the full model for the specified dirty regions.
    /// The mesher produces a full mesh, and this method extracts vertex/index sub-buffers
    /// for each region. When a region's geometry can't be efficiently extracted from the
    /// full mesh, a <see cref="MeshRegionUpdate"/> with <see cref="MeshRegionUpdateKind.FullReplace"/>
    /// is returned instead.
    /// <para>
    /// For this initial implementation, the strategy is:
    /// - Build a full mesh snapshot.
    /// - For each dirty region, compute the bounds and include geometry within those bounds.
    /// - Return update entries keyed by stable region identifiers.
    /// </para>
    /// </summary>
    public MeshIncrementalUpdate BuildIncrementalUpdate(
        VoxelModel model,
        IEnumerable<MeshRegionCoord> dirtyRegions,
        string modelId,
        string baseMeshId,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(model);

        var buildStopwatch = System.Diagnostics.Stopwatch.StartNew();

        var fullSnapshot = new MeshSnapshotService(_mesher).BuildSnapshot(model);
        var dirtySet = dirtyRegions as ICollection<MeshRegionCoord> ?? dirtyRegions.ToList();
        var updates = new List<MeshRegionUpdate>();

        foreach (var region in dirtySet)
        {
            var regionBounds = region.ToBounds(chunkSize);
            var regionUpdate = ExtractRegionGeometry(fullSnapshot, region, regionBounds, modelId);
            updates.Add(regionUpdate);
        }

        // If no dirty regions specified, do a full replace of the entire model
        if (updates.Count == 0)
        {
            updates.Add(new MeshRegionUpdate
            {
                RegionId = "all",
                UpdateKind = MeshRegionUpdateKind.FullReplace,
                Bounds = fullSnapshot.Bounds is not null
                    ? new RegionBounds(
                        fullSnapshot.Bounds.MinX, fullSnapshot.Bounds.MinY, fullSnapshot.Bounds.MinZ,
                        fullSnapshot.Bounds.MaxX, fullSnapshot.Bounds.MaxY, fullSnapshot.Bounds.MaxZ)
                    : new RegionBounds(0, 0, 0, 0, 0, 0),
                VertexOffset = 0,
                VertexCount = fullSnapshot.VertexCount,
                IndexOffset = 0,
                IndexCount = fullSnapshot.Indices.Length,
                Positions = fullSnapshot.Positions,
                Normals = fullSnapshot.Normals,
                Colors = fullSnapshot.Colors,
                PaletteIndices = fullSnapshot.PaletteIndices,
                Indices = fullSnapshot.Indices,
            });
        }

        buildStopwatch.Stop();

        return new MeshIncrementalUpdate
        {
            ModelId = modelId,
            BaseMeshId = baseMeshId,
            UpdateType = dirtySet.Count > 0 ? MeshUpdateType.Incremental : MeshUpdateType.FullReplace,
            ChangedRegions = updates,
            PayloadFormat = "json",
            FullVertexCount = fullSnapshot.VertexCount,
            FullIndexCount = fullSnapshot.Indices.Length,
            Metrics = new MeshUpdateMetrics
            {
                RegionCount = updates.Count,
                BuildMs = buildStopwatch.ElapsedMilliseconds,
                SerializeMs = 0,
            },
        };
    }

    /// <summary>
    /// Extract geometry for a single region from the full snapshot.
    /// For this implementation, we do a full replace per region (extract vertices
    /// whose positions fall within the region bounds).
    /// </summary>
    private static MeshRegionUpdate ExtractRegionGeometry(
        MeshSnapshot snapshot,
        MeshRegionCoord region,
        RegionBounds bounds,
        string modelId)
    {
        // Collect vertex indices whose position falls within the region bounds
        var regionVertices = new List<int>();
        var vertexToRegionIndex = new Dictionary<int, int>();

        for (int i = 0; i < snapshot.VertexCount; i++)
        {
            float vx = snapshot.Positions[i * 3];
            float vy = snapshot.Positions[i * 3 + 1];
            float vz = snapshot.Positions[i * 3 + 2];

            if (vx >= bounds.MinX && vx <= bounds.MaxX + 1 &&
                vy >= bounds.MinY && vy <= bounds.MaxY + 1 &&
                vz >= bounds.MinZ && vz <= bounds.MaxZ + 1)
            {
                vertexToRegionIndex[i] = regionVertices.Count;
                regionVertices.Add(i);
            }
        }

        if (regionVertices.Count == 0)
        {
            // Empty region — still report it so TS knows the region was processed
            return new MeshRegionUpdate
            {
                RegionId = region.ToString(),
                UpdateKind = MeshRegionUpdateKind.Incremental,
                Bounds = bounds,
                VertexOffset = 0,
                VertexCount = 0,
                IndexOffset = 0,
                IndexCount = 0,
                Positions = [],
                Normals = [],
                Colors = [],
                PaletteIndices = null,
                Indices = [],
            };
        }

        // Collect triangles (indices) where all three vertices are in the region
        var regionIndices = new List<int>();
        for (int i = 0; i < snapshot.Indices.Length; i += 3)
        {
            int i0 = snapshot.Indices[i];
            int i1 = snapshot.Indices[i + 1];
            int i2 = snapshot.Indices[i + 2];

            if (vertexToRegionIndex.ContainsKey(i0) &&
                vertexToRegionIndex.ContainsKey(i1) &&
                vertexToRegionIndex.ContainsKey(i2))
            {
                regionIndices.Add(vertexToRegionIndex[i0]);
                regionIndices.Add(vertexToRegionIndex[i1]);
                regionIndices.Add(vertexToRegionIndex[i2]);
            }
        }

        // Build compact per-region buffers
        var positions = new float[regionVertices.Count * 3];
        var normals = new float[regionVertices.Count * 3];
        var colors = new byte[regionVertices.Count * 4];
        byte[]? paletteIndices = snapshot.PaletteIndices is not null
            ? new byte[regionVertices.Count]
            : null;
        var indices = regionIndices.ToArray();

        for (int ri = 0; ri < regionVertices.Count; ri++)
        {
            int si = regionVertices[ri];
            positions[ri * 3] = snapshot.Positions[si * 3];
            positions[ri * 3 + 1] = snapshot.Positions[si * 3 + 1];
            positions[ri * 3 + 2] = snapshot.Positions[si * 3 + 2];

            normals[ri * 3] = snapshot.Normals[si * 3];
            normals[ri * 3 + 1] = snapshot.Normals[si * 3 + 1];
            normals[ri * 3 + 2] = snapshot.Normals[si * 3 + 2];

            colors[ri * 4] = snapshot.Colors[si * 4];
            colors[ri * 4 + 1] = snapshot.Colors[si * 4 + 1];
            colors[ri * 4 + 2] = snapshot.Colors[si * 4 + 2];
            colors[ri * 4 + 3] = snapshot.Colors[si * 4 + 3];

            if (paletteIndices is not null && snapshot.PaletteIndices is not null)
            {
                paletteIndices[ri] = snapshot.PaletteIndices[si];
            }
        }

        return new MeshRegionUpdate
        {
            RegionId = region.ToString(),
            UpdateKind = MeshRegionUpdateKind.Incremental,
            Bounds = bounds,
            VertexOffset = 0, // per-region buffers start at 0
            VertexCount = regionVertices.Count,
            IndexOffset = 0,
            IndexCount = indices.Length,
            Positions = positions,
            Normals = normals,
            Colors = colors,
            PaletteIndices = paletteIndices,
            Indices = indices,
        };
    }

    /// <summary>
    /// Floor division that works correctly for negative coordinates.
    /// Ensures region identifiers are stable for negative voxel positions.
    /// </summary>
    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return value < 0 && value % divisor != 0 ? q - 1 : q;
    }
}

/// <summary>
/// Stable coordinate identifier for a chunk/region within the voxel model grid.
/// Derived from the region's position in the chunk grid.
/// Distinct from <see cref="Core.RegionId"/> which is a label-based region identifier.
/// </summary>
public readonly record struct MeshRegionCoord(int Rx, int Ry, int Rz)
{
    /// <summary>
    /// Compute the region coordinate for a point, using the given chunk size.
    /// </summary>
    public static MeshRegionCoord FromPoint(Point3 point, int chunkSize)
    {
        return new MeshRegionCoord(
            FloorDiv(point.X, chunkSize),
            FloorDiv(point.Y, chunkSize),
            FloorDiv(point.Z, chunkSize));
    }

    private static int FloorDiv(int value, int divisor)
    {
        int q = value / divisor;
        return value < 0 && value % divisor != 0 ? q - 1 : q;
    }

    /// <summary>
    /// Convert this region coordinate to its world-space bounds.
    /// </summary>
    public RegionBounds ToBounds(int chunkSize)
    {
        return new RegionBounds(
            Rx * chunkSize,
            Ry * chunkSize,
            Rz * chunkSize,
            (Rx + 1) * chunkSize - 1,
            (Ry + 1) * chunkSize - 1,
            (Rz + 1) * chunkSize - 1);
    }

    public override string ToString() => $"{Rx}_{Ry}_{Rz}";
}

/// <summary>
/// Axis-aligned bounding box for a region or changed area.
/// </summary>
public sealed record RegionBounds(int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ);

/// <summary>
/// The type of mesh update being sent.
/// </summary>
public enum MeshUpdateType
{
    /// <summary>
    /// Partial update: only changed regions are included.
    /// TS should replace buffers for those regions without reloading the scene.
    /// </summary>
    Incremental,

    /// <summary>
    /// Full replacement: TS must replace the entire mesh.
    /// </summary>
    FullReplace,
}

/// <summary>
/// The type of a single region update within an incremental mesh update.
/// </summary>
public enum MeshRegionUpdateKind
{
    /// <summary>
    /// Partial region update: replace the buffer for this region.
    /// </summary>
    Incremental,

    /// <summary>
    /// Full replacement: replace the entire mesh for the model.
    /// </summary>
    FullReplace,
}

/// <summary>
/// Renderer-neutral incremental mesh update payload.
/// Contains geometry for dirty regions keyed by stable region identifiers.
/// </summary>
public sealed class MeshIncrementalUpdate
{
    /// <summary>
    /// The model identifier this update applies to.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The base mesh snapshot identifier this update is relative to.
    /// TS uses this to verify the update applies to the currently cached mesh.
    /// </summary>
    public required string BaseMeshId { get; init; }

    /// <summary>
    /// Whether this update is incremental (dirty regions only) or a full replacement.
    /// </summary>
    public required MeshUpdateType UpdateType { get; init; }

    /// <summary>
    /// Per-region geometry updates.
    /// </summary>
    public required IReadOnlyList<MeshRegionUpdate> ChangedRegions { get; init; }

    /// <summary>
    /// Payload format: "json" for this implementation.
    /// </summary>
    public required string PayloadFormat { get; init; }

    /// <summary>
    /// Total vertex count in the full model (for TS to validate buffer sizes).
    /// </summary>
    public required int FullVertexCount { get; init; }

    /// <summary>
    /// Total index count in the full model.
    /// </summary>
    public required int FullIndexCount { get; init; }

    /// <summary>
    /// Performance metrics for this update.
    /// </summary>
    public required MeshUpdateMetrics Metrics { get; set; }
}

/// <summary>
/// A single region's geometry update within an incremental mesh update.
/// </summary>
public sealed class MeshRegionUpdate
{
    /// <summary>
    /// Stable region identifier (e.g., "0_0_0" for region at chunk origin).
    /// </summary>
    public required string RegionId { get; init; }

    /// <summary>
    /// Whether this region update is incremental or a full replace.
    /// </summary>
    public required MeshRegionUpdateKind UpdateKind { get; init; }

    /// <summary>
    /// Spatial bounds of this region.
    /// </summary>
    public required RegionBounds Bounds { get; init; }

    /// <summary>
    /// Vertex offset within the full mesh buffer (0 for per-region buffers).
    /// </summary>
    public required int VertexOffset { get; init; }

    /// <summary>
    /// Number of vertices in this region.
    /// </summary>
    public required int VertexCount { get; init; }

    /// <summary>
    /// Index offset within the full mesh index buffer (0 for per-region buffers).
    /// </summary>
    public required int IndexOffset { get; init; }

    /// <summary>
    /// Number of indices in this region.
    /// </summary>
    public required int IndexCount { get; init; }

    /// <summary>
    /// Flat array of float32 vertex positions for this region.
    /// </summary>
    public required float[] Positions { get; init; }

    /// <summary>
    /// Flat array of float32 vertex normals for this region.
    /// </summary>
    public required float[] Normals { get; init; }

    /// <summary>
    /// Flat array of uint8 RGBA vertex colors for this region.
    /// </summary>
    public required byte[] Colors { get; init; }

    /// <summary>
    /// Per-vertex palette indices for this region. May be null.
    /// </summary>
    public byte[]? PaletteIndices { get; init; }

    /// <summary>
    /// Triangle indices for this region.
    /// </summary>
    public required int[] Indices { get; init; }
}

/// <summary>
/// Performance metrics for a mesh update.
/// </summary>
public sealed class MeshUpdateMetrics
{
    public required int RegionCount { get; init; }
    public required long BuildMs { get; init; }
    public required long SerializeMs { get; init; }
}