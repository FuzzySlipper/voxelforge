using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Benchmarking;
using VoxelForge.Core.Meshing;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App.Snapshots;

namespace VoxelForge.Evaluation;

/// <summary>
/// Headless C# renderer performance benchmark runner.
/// <para>
/// Measures mesh generation, snapshot/serialization, scene construction,
/// and editing round-trip latency across deterministic reference scenes.
/// Outputs JSON metrics to stdout, suitable for CI and comparison reports.
/// </para>
/// <para>
/// Does not depend on FNA/Myra or any rendering framework — all measurements
/// are at the Core/App service layer.
/// </para>
/// </summary>
public static class RendererBenchmark
{
    /// <summary>
    /// Run the renderer benchmark suite for the given scenes and write JSON results to
    /// the specified output writer.
    /// </summary>
    /// <param name="scenes">Scenes to benchmark. If empty, uses <see cref="RendererBenchmarkScenes.DefaultScenes"/>.</param>
    /// <param name="output">TextWriter for JSON metrics output.</param>
    /// <param name="includeExtraLarge">If true, include the extra-large checkerboard scene.</param>
    /// <param name="warmupTrials">Number of warmup iterations before measurement.</param>
    /// <param name="measurementTrials">Number of measurement iterations (reported as median).</param>
    /// <param name="includeSceneModels">If true, include the benchmark models in the output (as JSON hex).</param>
    public static void Run(
        SceneId[]? scenes = null,
        TextWriter? output = null,
        bool includeExtraLarge = false,
        int warmupTrials = 2,
        int measurementTrials = 5,
        bool includeSceneModels = false)
    {
        output ??= Console.Out;

        var sceneList = scenes is { Length: > 0 }
            ? scenes
            : includeExtraLarge ? RendererBenchmarkScenes.AllScenes : RendererBenchmarkScenes.DefaultScenes;

        var results = new List<SceneBenchmarkResult>(sceneList.Length);

        foreach (SceneId sceneId in sceneList)
        {
            var sceneResult = BenchmarkSingleScene(
                sceneId,
                warmupTrials,
                measurementTrials);
            results.Add(sceneResult);
        }

        var suiteResult = new BenchmarkSuiteResult
        {
            SchemaVersion = 1,
            BenchmarkName = "renderer-benchmark",
            Description = "Systematic renderer performance benchmarks for VoxelForge",
            GitCommit = TryReadGitCommit(),
            TimestampUtc = DateTimeOffset.UtcNow,
            WarmupTrials = warmupTrials,
            MeasurementTrials = measurementTrials,
            Scenes = results,
        };

        string json = JsonSerializer.Serialize(suiteResult, SuiteJsonContext.Default.BenchmarkSuiteResult);
        output.WriteLine(json);
    }

    private static SceneBenchmarkResult BenchmarkSingleScene(
        SceneId sceneId,
        int warmupTrials,
        int measurementTrials)
    {
        string name = sceneId.ToString();
        int expectedVoxels = RendererBenchmarkScenes.ExpectedVoxelCount(sceneId);

        // Build the scene reference model once
        VoxelModel model = RendererBenchmarkScenes.Build(sceneId);
        int actualVoxels = model.GetVoxelCount();

        var logger = NullLogger<GreedyMesher>.Instance;
        var greedyMesher = new GreedyMesher();
        var naiveMesher = new NaiveMesher();
        var snapshotService = new MeshSnapshotService(greedyMesher);

        // Warmup
        for (int i = 0; i < warmupTrials; i++)
        {
            greedyMesher.Build(model);
            naiveMesher.Build(model);
            snapshotService.BuildSnapshot(model);
        }

        // ── Greedy mesher measurements ──
        long[] greedyTimes = new long[measurementTrials];
        int greedyVertices = 0;
        int greedyTriangles = 0;
        for (int i = 0; i < measurementTrials; i++)
        {
            var sw = Stopwatch.StartNew();
            var mesh = greedyMesher.Build(model);
            sw.Stop();
            greedyTimes[i] = sw.ElapsedMilliseconds;
            greedyVertices = mesh.Vertices.Length;
            greedyTriangles = mesh.TriangleCount;
        }

        // ── Naive mesher measurements ──
        long[] naiveTimes = new long[measurementTrials];
        int naiveVertices = 0;
        int naiveTriangles = 0;
        for (int i = 0; i < measurementTrials; i++)
        {
            var sw = Stopwatch.StartNew();
            var mesh = naiveMesher.Build(model);
            sw.Stop();
            naiveTimes[i] = sw.ElapsedMilliseconds;
            naiveVertices = mesh.Vertices.Length;
            naiveTriangles = mesh.TriangleCount;
        }

        // ── Mesh snapshot measurements (GreedyMesher + MeshSnapshotService) ──
        long[] snapshotTimes = new long[measurementTrials];
        int snapshotVertices = 0;
        int snapshotTriangles = 0;
        int snapshotPositionsLength = 0;
        int snapshotColorsLength = 0;
        int snapshotIndicesLength = 0;
        for (int i = 0; i < measurementTrials; i++)
        {
            var sw = Stopwatch.StartNew();
            var snapshot = snapshotService.BuildSnapshot(model);
            sw.Stop();
            snapshotTimes[i] = sw.ElapsedMilliseconds;
            snapshotVertices = snapshot.VertexCount;
            snapshotTriangles = snapshot.TriangleCount;
            snapshotPositionsLength = snapshot.Positions.Length;
            snapshotColorsLength = snapshot.Colors.Length;
            snapshotIndicesLength = snapshot.Indices.Length;
        }

        // ── Model clone + mutation latency (single voxel edit round-trip) ──
        long[] mutationTimes = new long[measurementTrials];
        for (int i = 0; i < measurementTrials; i++)
        {
            // Measure: 1 set + 1 remove + 1 undo-model-restore
            // This simulates the C# side of an editing round-trip
            var clone1 = CloneModel(model);
            var sw = Stopwatch.StartNew();
            clone1.SetVoxel(new Point3(0, 0, 0), 2);
            clone1.RemoveVoxel(new Point3(0, 0, 0));
            // Re-mesh after mutation (what the renderer path would do)
            greedyMesher.Build(clone1);
            sw.Stop();
            mutationTimes[i] = sw.ElapsedMilliseconds;
        }

        // ── Serialization estimate: JSON size of snapshot data ──
        var snapshotForSize = snapshotService.BuildSnapshot(model);
        long estimatedJsonBytes = EstimateSnapshotJsonSize(snapshotForSize);

        return new SceneBenchmarkResult
        {
            SceneId = name,
            ExpectedVoxelCount = expectedVoxels,
            ActualVoxelCount = actualVoxels,
            MeshBounds = GetBoundsString(model.GetBounds()),
            GreedyMesher = new MesherMetrics
            {
                MedianMs = Median(greedyTimes),
                MinMs = greedyTimes.Min(),
                MaxMs = greedyTimes.Max(),
                VertexCount = greedyVertices,
                TriangleCount = greedyTriangles,
            },
            NaiveMesher = new MesherMetrics
            {
                MedianMs = Median(naiveTimes),
                MinMs = naiveTimes.Min(),
                MaxMs = naiveTimes.Max(),
                VertexCount = naiveVertices,
                TriangleCount = naiveTriangles,
            },
            MeshSnapshot = new SnapshotMetrics
            {
                MedianMs = Median(snapshotTimes),
                MinMs = snapshotTimes.Min(),
                MaxMs = snapshotTimes.Max(),
                VertexCount = snapshotVertices,
                TriangleCount = snapshotTriangles,
                PositionsLength = snapshotPositionsLength,
                ColorsLength = snapshotColorsLength,
                IndicesLength = snapshotIndicesLength,
                EstimatedJsonBytes = estimatedJsonBytes,
            },
            EditMutation = new MutationMetrics
            {
                MedianMs = Median(mutationTimes),
                MinMs = mutationTimes.Min(),
                MaxMs = mutationTimes.Max(),
            },
        };
    }

    private static VoxelModel CloneModel(VoxelModel original)
    {
        var clone = new VoxelModel(NullLogger<VoxelModel>.Instance);
        clone.GridHint = original.GridHint;
        foreach (var entry in original.Palette.Entries)
            clone.Palette.Set(entry.Key, entry.Value);
        foreach (var kvp in original.Voxels)
            clone.SetVoxel(kvp.Key, kvp.Value);
        return clone;
    }

    private static long Median(long[] values)
    {
        if (values.Length == 0) return 0;
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static string? GetBoundsString((Point3 Min, Point3 Max)? bounds)
    {
        if (bounds is null) return null;
        var (min, max) = bounds.Value;
        return $"({min.X},{min.Y},{min.Z})-({max.X},{max.Y},{max.Z})";
    }

    /// <summary>
    /// Rough estimate of JSON-serialized snapshot size (positions + normals + colors + indices).
    /// This is what gets transmitted over the bridge to Electron.
    /// </summary>
    private static long EstimateSnapshotJsonSize(MeshSnapshot snapshot)
    {
        // Rough: each float ~ 12-16 bytes in JSON (e.g. "-1.234567") plus comma/space
        // Each byte ~ 4 bytes in JSON (e.g. "255") plus comma/space
        // Each int ~ 4-10 bytes
        long estimate = 0;
        estimate += snapshot.Positions.Length * 12L;  // floats
        estimate += snapshot.Normals.Length * 12L;    // floats
        estimate += snapshot.Colors.Length * 4L;      // bytes as numbers
        if (snapshot.PaletteIndices is { } pi)
            estimate += pi.Length * 4L;                // bytes as numbers
        estimate += snapshot.Indices.Length * 8L;      // ints
        estimate += 256; // overhead brackets, keys, bounds
        return estimate;
    }

    private static string? TryReadGitCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { "rev-parse", "HEAD" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}

// ── JSON DTOs ──

[JsonSerializable(typeof(BenchmarkSuiteResult))]
internal sealed partial class SuiteJsonContext : JsonSerializerContext;

public sealed class BenchmarkSuiteResult
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("benchmark_name")]
    public required string BenchmarkName { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("git_commit")]
    public string? GitCommit { get; init; }

    [JsonPropertyName("timestamp_utc")]
    public DateTimeOffset TimestampUtc { get; init; }

    [JsonPropertyName("warmup_trials")]
    public int WarmupTrials { get; init; }

    [JsonPropertyName("measurement_trials")]
    public int MeasurementTrials { get; init; }

    [JsonPropertyName("scenes")]
    public required IReadOnlyList<SceneBenchmarkResult> Scenes { get; init; }
}

public sealed class SceneBenchmarkResult
{
    [JsonPropertyName("scene_id")]
    public required string SceneId { get; init; }

    [JsonPropertyName("expected_voxel_count")]
    public int ExpectedVoxelCount { get; init; }

    [JsonPropertyName("actual_voxel_count")]
    public int ActualVoxelCount { get; init; }

    [JsonPropertyName("mesh_bounds")]
    public string? MeshBounds { get; init; }

    [JsonPropertyName("greedy_mesher")]
    public required MesherMetrics GreedyMesher { get; init; }

    [JsonPropertyName("naive_mesher")]
    public required MesherMetrics NaiveMesher { get; init; }

    [JsonPropertyName("mesh_snapshot")]
    public required SnapshotMetrics MeshSnapshot { get; init; }

    [JsonPropertyName("edit_mutation")]
    public required MutationMetrics EditMutation { get; init; }
}

public sealed class MesherMetrics
{
    [JsonPropertyName("median_ms")]
    public long MedianMs { get; init; }

    [JsonPropertyName("min_ms")]
    public long MinMs { get; init; }

    [JsonPropertyName("max_ms")]
    public long MaxMs { get; init; }

    [JsonPropertyName("vertex_count")]
    public int VertexCount { get; init; }

    [JsonPropertyName("triangle_count")]
    public int TriangleCount { get; init; }
}

public sealed class SnapshotMetrics
{
    [JsonPropertyName("median_ms")]
    public long MedianMs { get; init; }

    [JsonPropertyName("min_ms")]
    public long MinMs { get; init; }

    [JsonPropertyName("max_ms")]
    public long MaxMs { get; init; }

    [JsonPropertyName("vertex_count")]
    public int VertexCount { get; init; }

    [JsonPropertyName("triangle_count")]
    public int TriangleCount { get; init; }

    [JsonPropertyName("positions_length")]
    public int PositionsLength { get; init; }

    [JsonPropertyName("colors_length")]
    public int ColorsLength { get; init; }

    [JsonPropertyName("indices_length")]
    public int IndicesLength { get; init; }

    [JsonPropertyName("estimated_json_bytes")]
    public long EstimatedJsonBytes { get; init; }
}

public sealed class MutationMetrics
{
    [JsonPropertyName("median_ms")]
    public long MedianMs { get; init; }

    [JsonPropertyName("min_ms")]
    public long MinMs { get; init; }

    [JsonPropertyName("max_ms")]
    public long MaxMs { get; init; }
}
