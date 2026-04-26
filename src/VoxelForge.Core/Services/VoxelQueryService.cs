using System.Text.Json.Serialization;

namespace VoxelForge.Core.Services;

public readonly record struct VoxelBoxQueryRequest(Point3 Min, Point3 Max);

public readonly record struct VoxelSphereQueryRequest(Point3 Center, float Radius);

public readonly record struct VoxelQueryEntry(Point3 Position, byte PaletteIndex, string MaterialName);

public sealed class VoxelValueResult
{
    public required Point3 Position { get; init; }
    public byte? PaletteIndex { get; init; }
    public string? MaterialName { get; init; }
}

public sealed class VoxelQueryResult
{
    public required IReadOnlyList<VoxelQueryEntry> Voxels { get; init; }
    public int Count => Voxels.Count;
}

public sealed class ModelInfoResult
{
    [JsonPropertyName("voxelCount")]
    public required int VoxelCount { get; init; }

    [JsonPropertyName("gridHint")]
    public required int GridHint { get; init; }

    [JsonPropertyName("bounds")]
    public ModelBoundsInfo? Bounds { get; init; }

    [JsonPropertyName("paletteEntries")]
    public required IReadOnlyList<PaletteEntryInfo> PaletteEntries { get; init; }

    [JsonPropertyName("regions")]
    public required IReadOnlyList<RegionInfo> Regions { get; init; }

    [JsonPropertyName("animationClips")]
    public required IReadOnlyList<AnimationClipInfo> AnimationClips { get; init; }
}

public sealed class ModelBoundsInfo
{
    [JsonPropertyName("min")]
    public required string Min { get; init; }

    [JsonPropertyName("max")]
    public required string Max { get; init; }
}

public sealed class PaletteEntryInfo
{
    [JsonPropertyName("index")]
    public required byte PaletteIndex { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("color")]
    public required string Color { get; init; }
}

public sealed class RegionInfo
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("voxelCount")]
    public required int VoxelCount { get; init; }
}

public sealed class AnimationClipInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("frameCount")]
    public required int FrameCount { get; init; }

    [JsonPropertyName("frameRate")]
    public required int FrameRate { get; init; }
}

public sealed class VoxelAreaInfo
{
    [JsonPropertyName("voxels")]
    public required IReadOnlyList<VoxelAreaEntryInfo> Voxels { get; init; }

    [JsonPropertyName("count")]
    public int Count => Voxels.Count;
}

public sealed class VoxelAreaEntryInfo
{
    [JsonPropertyName("x")]
    public required int X { get; init; }

    [JsonPropertyName("y")]
    public required int Y { get; init; }

    [JsonPropertyName("z")]
    public required int Z { get; init; }

    [JsonPropertyName("i")]
    public required byte PaletteIndex { get; init; }

    [JsonPropertyName("regionId")]
    public string? RegionId { get; init; }

    [JsonPropertyName("regionName")]
    public string? RegionName { get; init; }
}

/// <summary>
/// Stateless domain/query service for model descriptions, voxel lookups, and counts.
/// </summary>
public sealed class VoxelQueryService
{
    public string DescribeModel(VoxelModel model, LabelIndex labels, IReadOnlyList<AnimationClip> clips)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(clips);

        var bounds = model.GetBounds();
        var lines = new List<string>
        {
            $"Voxel model with {model.GetVoxelCount()} voxels, grid hint {model.GridHint}.",
        };

        if (bounds is not null)
            lines.Add($"Bounds: ({bounds.Value.Min.X},{bounds.Value.Min.Y},{bounds.Value.Min.Z}) to ({bounds.Value.Max.X},{bounds.Value.Max.Y},{bounds.Value.Max.Z}).");

        if (model.Palette.Count > 0)
        {
            lines.Add($"Palette: {model.Palette.Count} colors.");
            foreach (var entry in model.Palette.Entries)
                lines.Add($"  [{entry.Key}] {entry.Value.Name} ({entry.Value.Color.R},{entry.Value.Color.G},{entry.Value.Color.B})");
        }

        if (labels.Regions.Count > 0)
        {
            lines.Add($"Regions: {labels.Regions.Count}.");
            foreach (var entry in labels.Regions)
                lines.Add($"  {entry.Value.Name}: {entry.Value.Voxels.Count} voxels");
        }

        var distribution = new Dictionary<byte, int>();
        foreach (var entry in model.Voxels)
        {
            distribution.TryGetValue(entry.Value, out int count);
            distribution[entry.Value] = count + 1;
        }

        if (distribution.Count > 0)
        {
            lines.Add("Material distribution:");
            var distributionEntries = new List<KeyValuePair<byte, int>>(distribution);
            distributionEntries.Sort(CompareMaterialCountsDescending);
            int voxelCount = model.GetVoxelCount();
            foreach (var entry in distributionEntries)
            {
                var name = model.Palette.Get(entry.Key)?.Name ?? $"index_{entry.Key}";
                lines.Add($"  {name}: {entry.Value} voxels ({100.0 * entry.Value / voxelCount:F1}%)");
            }
        }

        return string.Join("\n", lines);
    }

    public ModelInfoResult GetModelInfo(VoxelModel model, LabelIndex labels, IReadOnlyList<AnimationClip> clips)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(clips);

        var bounds = model.GetBounds();
        ModelBoundsInfo? boundsInfo = bounds is null
            ? null
            : new ModelBoundsInfo
            {
                Min = $"{bounds.Value.Min}",
                Max = $"{bounds.Value.Max}",
            };

        var paletteEntries = new List<PaletteEntryInfo>();
        foreach (var entry in model.Palette.Entries)
        {
            paletteEntries.Add(new PaletteEntryInfo
            {
                PaletteIndex = entry.Key,
                Name = entry.Value.Name,
                Color = $"({entry.Value.Color.R},{entry.Value.Color.G},{entry.Value.Color.B})",
            });
        }

        var regions = new List<RegionInfo>();
        foreach (var entry in labels.Regions)
        {
            regions.Add(new RegionInfo
            {
                Id = entry.Key.Value,
                Name = entry.Value.Name,
                VoxelCount = entry.Value.Voxels.Count,
            });
        }

        var clipInfos = new List<AnimationClipInfo>();
        foreach (var clip in clips)
        {
            clipInfos.Add(new AnimationClipInfo
            {
                Name = clip.Name,
                FrameCount = clip.Frames.Count,
                FrameRate = clip.FrameRate,
            });
        }

        return new ModelInfoResult
        {
            VoxelCount = model.GetVoxelCount(),
            GridHint = model.GridHint,
            Bounds = boundsInfo,
            PaletteEntries = paletteEntries,
            Regions = regions,
            AnimationClips = clipInfos,
        };
    }

    public VoxelAreaInfo GetVoxelsInArea(VoxelModel model, VoxelBoxQueryRequest request)
    {
        return GetVoxelsInArea(model, labels: null, request: request);
    }

    public VoxelAreaInfo GetVoxelsInArea(VoxelModel model, LabelIndex? labels, VoxelBoxQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);

        var voxels = new List<VoxelAreaEntryInfo>();
        foreach (var entry in model.Voxels)
        {
            var position = entry.Key;
            if (position.X >= request.Min.X && position.X <= request.Max.X &&
                position.Y >= request.Min.Y && position.Y <= request.Max.Y &&
                position.Z >= request.Min.Z && position.Z <= request.Max.Z)
            {
                string? regionId = null;
                string? regionName = null;
                var region = labels?.GetRegion(position);
                if (region.HasValue)
                {
                    regionId = region.Value.Value;
                    if (labels?.Regions.TryGetValue(region.Value, out var regionDef) == true)
                        regionName = regionDef.Name;
                }

                voxels.Add(new VoxelAreaEntryInfo
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    PaletteIndex = entry.Value,
                    RegionId = regionId,
                    RegionName = regionName,
                });
            }
        }

        return new VoxelAreaInfo { Voxels = voxels };
    }

    public VoxelValueResult GetVoxel(VoxelModel model, Point3 position)
    {
        ArgumentNullException.ThrowIfNull(model);

        var value = model.GetVoxel(position);
        string? materialName = value.HasValue ? model.Palette.Get(value.Value)?.Name ?? "unknown" : null;
        return new VoxelValueResult
        {
            Position = position,
            PaletteIndex = value,
            MaterialName = materialName,
        };
    }

    public VoxelQueryResult QueryBox(VoxelModel model, VoxelBoxQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);

        var voxels = new List<VoxelQueryEntry>();
        for (int x = request.Min.X; x <= request.Max.X; x++)
        for (int y = request.Min.Y; y <= request.Max.Y; y++)
        for (int z = request.Min.Z; z <= request.Max.Z; z++)
        {
            var position = new Point3(x, y, z);
            var value = model.GetVoxel(position);
            if (value.HasValue)
                voxels.Add(CreateEntry(model, position, value.Value));
        }

        return new VoxelQueryResult { Voxels = voxels };
    }

    public VoxelQueryResult QuerySphere(VoxelModel model, VoxelSphereQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);

        float radiusSquared = request.Radius * request.Radius;
        int integerRadius = (int)MathF.Ceiling(request.Radius);
        var voxels = new List<VoxelQueryEntry>();

        for (int x = request.Center.X - integerRadius; x <= request.Center.X + integerRadius; x++)
        for (int y = request.Center.Y - integerRadius; y <= request.Center.Y + integerRadius; y++)
        for (int z = request.Center.Z - integerRadius; z <= request.Center.Z + integerRadius; z++)
        {
            float dx = x - request.Center.X;
            float dy = y - request.Center.Y;
            float dz = z - request.Center.Z;
            if (dx * dx + dy * dy + dz * dz > radiusSquared)
                continue;

            var position = new Point3(x, y, z);
            var value = model.GetVoxel(position);
            if (value.HasValue)
                voxels.Add(CreateEntry(model, position, value.Value));
        }

        return new VoxelQueryResult { Voxels = voxels };
    }

    public int CountVoxels(VoxelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetVoxelCount();
    }

    public int CountVoxelsInBox(VoxelModel model, VoxelBoxQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);

        int count = 0;
        for (int x = request.Min.X; x <= request.Max.X; x++)
        for (int y = request.Min.Y; y <= request.Max.Y; y++)
        for (int z = request.Min.Z; z <= request.Max.Z; z++)
        {
            if (model.GetVoxel(new Point3(x, y, z)).HasValue)
                count++;
        }

        return count;
    }

    public int CountVoxelsByPalette(VoxelModel model, byte paletteIndex)
    {
        ArgumentNullException.ThrowIfNull(model);

        int count = 0;
        foreach (var entry in model.Voxels)
        {
            if (entry.Value == paletteIndex)
                count++;
        }

        return count;
    }

    private static VoxelQueryEntry CreateEntry(VoxelModel model, Point3 position, byte paletteIndex)
    {
        var materialName = model.Palette.Get(paletteIndex)?.Name ?? "?";
        return new VoxelQueryEntry(position, paletteIndex, materialName);
    }

    private static int CompareMaterialCountsDescending(KeyValuePair<byte, int> left, KeyValuePair<byte, int> right)
    {
        int valueComparison = right.Value.CompareTo(left.Value);
        return valueComparison != 0 ? valueComparison : left.Key.CompareTo(right.Key);
    }
}
