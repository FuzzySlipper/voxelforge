using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkComparisonEntry
{
    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("variant_id")]
    public required string VariantId { get; init; }

    [JsonPropertyName("trial")]
    public int Trial { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("voxel_count")]
    public int VoxelCount { get; init; }

    [JsonPropertyName("bounds")]
    public required string Bounds { get; init; }

    [JsonPropertyName("palette_usage")]
    public IReadOnlyList<BenchmarkPaletteUsageMetric> PaletteUsage { get; init; } = [];

    [JsonPropertyName("region_count")]
    public int RegionCount { get; init; }

    [JsonPropertyName("labeled_voxel_count")]
    public int LabeledVoxelCount { get; init; }

    [JsonPropertyName("tool_call_count")]
    public int ToolCallCount { get; init; }

    [JsonPropertyName("failed_tool_call_count")]
    public int FailedToolCallCount { get; init; }

    [JsonPropertyName("normalized_voxel_hash")]
    public required string NormalizedVoxelHash { get; init; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; init; }

    [JsonPropertyName("artifacts")]
    public required BenchmarkComparisonArtifactLinks Artifacts { get; init; }
}

public sealed class BenchmarkComparisonArtifactLinks
{
    [JsonPropertyName("run_manifest")]
    public required string RunManifest { get; init; }

    [JsonPropertyName("final_vforge")]
    public required string FinalVforge { get; init; }

    [JsonPropertyName("metrics")]
    public required string Metrics { get; init; }

    [JsonPropertyName("run_summary")]
    public required string RunSummary { get; init; }

    [JsonPropertyName("transcripts")]
    public required string Transcripts { get; init; }
}

public sealed class BenchmarkComparisonReport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("suite_id")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("run_count")]
    public int RunCount { get; init; }

    [JsonPropertyName("entries")]
    public required IReadOnlyList<BenchmarkComparisonEntry> Entries { get; init; }
}

public sealed class BenchmarkComparisonService
{
    public BenchmarkComparisonReport CompareAndWrite(string suiteDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteDirectory);

        BenchmarkComparisonReport report = BuildReport(suiteDirectory);
        WriteJson(Path.Combine(suiteDirectory, "comparison.json"), report);
        WriteMarkdown(Path.Combine(suiteDirectory, "comparison.md"), report);
        return report;
    }

    public BenchmarkComparisonReport BuildReport(string suiteDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteDirectory);

        string suiteManifestPath = Path.Combine(suiteDirectory, "suite-manifest.json");
        BenchmarkSuiteManifest? suiteManifest = JsonSerializer.Deserialize<BenchmarkSuiteManifest>(
            File.ReadAllText(suiteManifestPath),
            BenchmarkJson.ReadOptions);
        if (suiteManifest is null)
            throw new InvalidOperationException($"Could not read suite manifest: {suiteManifestPath}");

        var entries = new List<BenchmarkComparisonEntry>();
        string[] manifestPaths = Directory.GetFiles(suiteDirectory, "run-manifest.json", SearchOption.AllDirectories);
        Array.Sort(manifestPaths, StringComparer.Ordinal);

        for (int i = 0; i < manifestPaths.Length; i++)
        {
            string manifestPath = manifestPaths[i];
            BenchmarkRunManifest? manifest = JsonSerializer.Deserialize<BenchmarkRunManifest>(
                File.ReadAllText(manifestPath),
                BenchmarkJson.ReadOptions);
            if (manifest is null)
                throw new InvalidOperationException($"Could not read run manifest: {manifestPath}");

            string runDirectory = Path.GetDirectoryName(manifestPath)!;
            string metricsPath = Path.Combine(runDirectory, "outputs", "metrics.json");
            BenchmarkModelMetrics? metrics = JsonSerializer.Deserialize<BenchmarkModelMetrics>(
                File.ReadAllText(metricsPath),
                BenchmarkJson.ReadOptions);
            if (metrics is null)
                throw new InvalidOperationException($"Could not read metrics: {metricsPath}");

            entries.Add(new BenchmarkComparisonEntry
            {
                CaseId = manifest.CaseId,
                VariantId = manifest.VariantId,
                Trial = manifest.Trial,
                Status = manifest.Status,
                VoxelCount = metrics.VoxelCount,
                Bounds = FormatBounds(metrics),
                PaletteUsage = metrics.PaletteUsage,
                RegionCount = metrics.RegionCount,
                LabeledVoxelCount = metrics.LabeledVoxelCount,
                ToolCallCount = metrics.ToolCallCount,
                FailedToolCallCount = metrics.FailedToolCallCount,
                NormalizedVoxelHash = metrics.NormalizedVoxelHash,
                FailureMessage = ReadFailureMessage(runDirectory),
                Artifacts = new BenchmarkComparisonArtifactLinks
                {
                    RunManifest = RelativePath(suiteDirectory, manifestPath),
                    FinalVforge = RelativePath(suiteDirectory, Path.Combine(runDirectory, "outputs", "final.vforge")),
                    Metrics = RelativePath(suiteDirectory, metricsPath),
                    RunSummary = RelativePath(suiteDirectory, Path.Combine(runDirectory, "reports", "run-summary.md")),
                    Transcripts = RelativePath(suiteDirectory, Path.Combine(runDirectory, "transcripts")),
                },
            });
        }

        entries.Sort(CompareEntries);
        return new BenchmarkComparisonReport
        {
            SuiteId = suiteManifest.SuiteId,
            RunCount = entries.Count,
            Entries = entries,
        };
    }

    private static int CompareEntries(BenchmarkComparisonEntry left, BenchmarkComparisonEntry right)
    {
        int result = string.CompareOrdinal(left.CaseId, right.CaseId);
        if (result != 0)
            return result;

        result = string.CompareOrdinal(left.VariantId, right.VariantId);
        if (result != 0)
            return result;

        return left.Trial.CompareTo(right.Trial);
    }

    private static string? ReadFailureMessage(string runDirectory)
    {
        string failurePath = Path.Combine(runDirectory, "outputs", "failure.json");
        if (!File.Exists(failurePath))
            return null;

        BenchmarkFailureArtifact? failure = JsonSerializer.Deserialize<BenchmarkFailureArtifact>(
            File.ReadAllText(failurePath),
            BenchmarkJson.ReadOptions);
        return failure?.Message;
    }

    private static string RelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string FormatBounds(BenchmarkModelMetrics metrics)
    {
        return metrics.BoundsMin is null || metrics.BoundsMax is null
            ? "empty"
            : $"{metrics.BoundsMin}..{metrics.BoundsMax}";
    }

    private static void WriteJson<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, BenchmarkJson.WriteIndentedOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private static void WriteMarkdown(string path, BenchmarkComparisonReport report)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine($"# Benchmark comparison: {report.SuiteId}");
        writer.WriteLine();
        writer.WriteLine("## Summary");
        writer.WriteLine();
        writer.WriteLine("| Case | Variant | Trial | Status | Voxels | Bounds | Palette usage | Regions | Labeled voxels | Tools | Failed tools | Hash | Artifacts |");
        writer.WriteLine("|---|---|---:|---|---:|---|---|---:|---:|---:|---:|---|---|");

        for (int i = 0; i < report.Entries.Count; i++)
        {
            BenchmarkComparisonEntry entry = report.Entries[i];
            writer.Write("| ");
            writer.Write(entry.CaseId);
            writer.Write(" | ");
            writer.Write(entry.VariantId);
            writer.Write(" | ");
            writer.Write(entry.Trial);
            writer.Write(" | ");
            writer.Write(entry.Status);
            writer.Write(" | ");
            writer.Write(entry.VoxelCount);
            writer.Write(" | ");
            writer.Write(entry.Bounds);
            writer.Write(" | ");
            writer.Write(FormatPaletteUsage(entry.PaletteUsage));
            writer.Write(" | ");
            writer.Write(entry.RegionCount);
            writer.Write(" | ");
            writer.Write(entry.LabeledVoxelCount);
            writer.Write(" | ");
            writer.Write(entry.ToolCallCount);
            writer.Write(" | ");
            writer.Write(entry.FailedToolCallCount);
            writer.Write(" | ");
            writer.Write(ShortHash(entry.NormalizedVoxelHash));
            writer.Write(" | ");
            writer.Write($"[summary]({entry.Artifacts.RunSummary}), [final]({entry.Artifacts.FinalVforge}), [metrics]({entry.Artifacts.Metrics}), [transcripts]({entry.Artifacts.Transcripts})");
            writer.WriteLine(" |");
        }

        writer.WriteLine();
        writer.WriteLine("## Failed runs");
        writer.WriteLine();
        bool anyFailure = false;
        for (int i = 0; i < report.Entries.Count; i++)
        {
            BenchmarkComparisonEntry entry = report.Entries[i];
            if (string.Equals(entry.Status, "succeeded", StringComparison.Ordinal))
                continue;

            anyFailure = true;
            writer.Write("- ");
            writer.Write(entry.CaseId);
            writer.Write(" / ");
            writer.Write(entry.VariantId);
            writer.Write(" / trial ");
            writer.Write(entry.Trial);
            writer.Write(": ");
            writer.WriteLine(entry.FailureMessage ?? entry.Status);
        }

        if (!anyFailure)
            writer.WriteLine("- None");
    }

    private static string FormatPaletteUsage(IReadOnlyList<BenchmarkPaletteUsageMetric> usage)
    {
        if (usage.Count == 0)
            return "none";

        var parts = new string[usage.Count];
        for (int i = 0; i < usage.Count; i++)
        {
            BenchmarkPaletteUsageMetric metric = usage[i];
            string name = string.IsNullOrWhiteSpace(metric.Name) ? "unnamed" : metric.Name!;
            parts[i] = FormattableString.Invariant($"{metric.PaletteIndex}:{name}={metric.VoxelCount}");
        }

        return string.Join(", ", parts);
    }

    private static string ShortHash(string hash)
    {
        return hash.Length <= 12 ? hash : hash[..12];
    }
}
