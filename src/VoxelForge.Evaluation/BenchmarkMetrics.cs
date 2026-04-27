using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using VoxelForge.Core;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkPointMetrics
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("z")]
    public int Z { get; init; }

    public static BenchmarkPointMetrics FromPoint(Point3 point)
    {
        return new BenchmarkPointMetrics
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z,
        };
    }

    public override string ToString()
    {
        return $"({X},{Y},{Z})";
    }
}

public sealed class BenchmarkPaletteUsageMetric
{
    [JsonPropertyName("palette_index")]
    public byte PaletteIndex { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("voxel_count")]
    public int VoxelCount { get; init; }
}

public sealed class BenchmarkModelMetrics
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("voxel_count")]
    public int VoxelCount { get; init; }

    [JsonPropertyName("bounds_min")]
    public BenchmarkPointMetrics? BoundsMin { get; init; }

    [JsonPropertyName("bounds_max")]
    public BenchmarkPointMetrics? BoundsMax { get; init; }

    [JsonPropertyName("grid_hint")]
    public int GridHint { get; init; }

    [JsonPropertyName("palette_usage")]
    public IReadOnlyList<BenchmarkPaletteUsageMetric> PaletteUsage { get; init; } = [];

    [JsonPropertyName("region_count")]
    public int RegionCount { get; init; }

    [JsonPropertyName("labeled_voxel_count")]
    public int LabeledVoxelCount { get; init; }

    [JsonPropertyName("animation_clip_count")]
    public int AnimationClipCount { get; init; }

    [JsonPropertyName("connected_component_count_6")]
    public int ConnectedComponentCount6 { get; init; }

    [JsonPropertyName("tool_call_count")]
    public int ToolCallCount { get; init; }

    [JsonPropertyName("failed_tool_call_count")]
    public int FailedToolCallCount { get; init; }

    [JsonPropertyName("undoable_mutation_count")]
    public int UndoableMutationCount { get; init; }

    [JsonPropertyName("final_model_sha256")]
    public string? FinalModelSha256 { get; init; }

    [JsonPropertyName("normalized_voxel_hash")]
    public required string NormalizedVoxelHash { get; init; }
}

public sealed class BenchmarkMetricsOptions
{
    public int ToolCallCount { get; init; }
    public int FailedToolCallCount { get; init; }
    public int UndoableMutationCount { get; init; }
    public string? FinalModelSha256 { get; init; }
}

public sealed class BenchmarkMetricsService
{
    private static readonly Point3[] SixConnectedOffsets =
    [
        new(1, 0, 0),
        new(-1, 0, 0),
        new(0, 1, 0),
        new(0, -1, 0),
        new(0, 0, 1),
        new(0, 0, -1),
    ];

    public BenchmarkModelMetrics Compute(
        VoxelModel model,
        LabelIndex labels,
        IReadOnlyList<AnimationClip> clips,
        BenchmarkMetricsOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(options);

        var bounds = model.GetBounds();
        return new BenchmarkModelMetrics
        {
            VoxelCount = model.GetVoxelCount(),
            BoundsMin = bounds is null ? null : BenchmarkPointMetrics.FromPoint(bounds.Value.Min),
            BoundsMax = bounds is null ? null : BenchmarkPointMetrics.FromPoint(bounds.Value.Max),
            GridHint = model.GridHint,
            PaletteUsage = BuildPaletteUsage(model),
            RegionCount = labels.Regions.Count,
            LabeledVoxelCount = CountLabeledVoxels(labels),
            AnimationClipCount = clips.Count,
            ConnectedComponentCount6 = CountConnectedComponents6(model),
            ToolCallCount = options.ToolCallCount,
            FailedToolCallCount = options.FailedToolCallCount,
            UndoableMutationCount = options.UndoableMutationCount,
            FinalModelSha256 = options.FinalModelSha256,
            NormalizedVoxelHash = ComputeNormalizedVoxelHash(model),
        };
    }

    public string ComputeNormalizedVoxelHash(VoxelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var lines = new List<string>(model.Voxels.Count);
        foreach (var entry in model.Voxels)
        {
            Point3 point = entry.Key;
            lines.Add(FormattableString.Invariant($"{point.X},{point.Y},{point.Z},{entry.Value}"));
        }

        lines.Sort(StringComparer.Ordinal);
        string canonical = string.Join('\n', lines);
        return ComputeSha256(canonical);
    }

    public string ComputeFileSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = File.ReadAllBytes(path);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public string ComputeTextSha256(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ComputeSha256(text);
    }

    private static IReadOnlyList<BenchmarkPaletteUsageMetric> BuildPaletteUsage(VoxelModel model)
    {
        var counts = new SortedDictionary<byte, int>();
        foreach (byte paletteIndex in model.Voxels.Values)
        {
            counts.TryGetValue(paletteIndex, out int count);
            counts[paletteIndex] = count + 1;
        }

        var usage = new List<BenchmarkPaletteUsageMetric>(counts.Count);
        foreach (var entry in counts)
        {
            usage.Add(new BenchmarkPaletteUsageMetric
            {
                PaletteIndex = entry.Key,
                Name = model.Palette.Get(entry.Key)?.Name,
                VoxelCount = entry.Value,
            });
        }

        return usage;
    }

    private static int CountLabeledVoxels(LabelIndex labels)
    {
        var labeled = new HashSet<Point3>();
        foreach (var entry in labels.Regions)
        {
            foreach (Point3 point in entry.Value.Voxels)
                labeled.Add(point);
        }

        return labeled.Count;
    }

    private static int CountConnectedComponents6(VoxelModel model)
    {
        if (model.Voxels.Count == 0)
            return 0;

        var occupied = new HashSet<Point3>(model.Voxels.Keys);
        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();
        int components = 0;

        foreach (Point3 start in occupied)
        {
            if (!visited.Add(start))
                continue;

            components++;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Point3 current = queue.Dequeue();
                for (int i = 0; i < SixConnectedOffsets.Length; i++)
                {
                    Point3 offset = SixConnectedOffsets[i];
                    var next = new Point3(current.X + offset.X, current.Y + offset.Y, current.Z + offset.Z);
                    if (occupied.Contains(next) && visited.Add(next))
                        queue.Enqueue(next);
                }
            }
        }

        return components;
    }

    private static string ComputeSha256(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
