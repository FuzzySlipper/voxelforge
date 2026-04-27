using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;
using VoxelForge.Evaluation;

namespace VoxelForge.Evaluation.Tests;

public sealed class BenchmarkArtifactTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 4, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MetricsService_ComputesDeterministicVoxelHashAndCounts()
    {
        VoxelModel modelA = CreateModel();
        modelA.SetVoxel(new Point3(10, 0, 0), 2);
        modelA.SetVoxel(new Point3(0, 0, 0), 1);
        modelA.SetVoxel(new Point3(1, 0, 0), 1);

        VoxelModel modelB = CreateModel();
        modelB.SetVoxel(new Point3(1, 0, 0), 1);
        modelB.SetVoxel(new Point3(0, 0, 0), 1);
        modelB.SetVoxel(new Point3(10, 0, 0), 2);

        var labels = CreateLabels([new Point3(0, 0, 0), new Point3(1, 0, 0)]);
        var service = new BenchmarkMetricsService();

        BenchmarkModelMetrics metricsA = service.Compute(modelA, labels, [], new BenchmarkMetricsOptions
        {
            ToolCallCount = 4,
            FailedToolCallCount = 1,
            UndoableMutationCount = 2,
            FinalModelSha256 = "model-sha",
        });
        BenchmarkModelMetrics metricsB = service.Compute(modelB, labels, [], new BenchmarkMetricsOptions());

        Assert.Equal(metricsA.NormalizedVoxelHash, metricsB.NormalizedVoxelHash);
        Assert.Equal(3, metricsA.VoxelCount);
        Assert.Equal(new[] { 1, 2 }, metricsA.PaletteUsage.Select(entry => (int)entry.PaletteIndex).ToArray());
        Assert.Equal(2, metricsA.PaletteUsage[0].VoxelCount);
        Assert.Equal(1, metricsA.RegionCount);
        Assert.Equal(2, metricsA.LabeledVoxelCount);
        Assert.Equal(2, metricsA.ConnectedComponentCount6);
        Assert.Equal(4, metricsA.ToolCallCount);
        Assert.Equal(1, metricsA.FailedToolCallCount);
        Assert.Equal(2, metricsA.UndoableMutationCount);
        Assert.Equal("model-sha", metricsA.FinalModelSha256);
    }

    [Fact]
    public void ArtifactWriter_CreatesDocumentedLayoutAndDoesNotOverwriteTimestampedRuns()
    {
        using var temp = new TemporaryDirectory();
        CreateInputFiles(temp.RootPath);
        BenchmarkRunPlan plan = CreatePlan(Path.Combine(temp.RootPath, "artifacts"), "stdio");
        var writer = new BenchmarkArtifactWriter();

        BenchmarkSuiteArtifactContext firstSuite = writer.CreateSuite(plan, FixedTimestamp);
        BenchmarkSuiteArtifactContext secondSuite = writer.CreateSuite(plan, FixedTimestamp);

        Assert.NotEqual(firstSuite.RootPath, secondSuite.RootPath);
        Assert.EndsWith("20260427T000000Z", firstSuite.RootPath, StringComparison.Ordinal);
        Assert.EndsWith("20260427T000000Z-001", secondSuite.RootPath, StringComparison.Ordinal);

        BenchmarkRunArtifactResult result = writer.WriteRunArtifacts(firstSuite, CreateArtifactRequest(plan.Runs[0], "succeeded"), temp.RootPath);

        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "run-manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "inputs", "prompt.md")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "inputs", "system-prompt.md")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "inputs", "palette.json")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "inputs", "initial.vforge")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "inputs", "runset-fragment.json")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "transcripts", "conversation.jsonl")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "transcripts", "tool-calls.jsonl")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "transcripts", "stdio.jsonl")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "outputs", "final.vforge")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "outputs", "model-info.json")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "outputs", "metrics.json")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "outputs", "voxel-hash.txt")));
        Assert.True(File.Exists(Path.Combine(result.RunDirectory, "reports", "run-summary.md")));

        BenchmarkRunManifest manifest = ReadJson<BenchmarkRunManifest>(Path.Combine(result.RunDirectory, "run-manifest.json"));
        Assert.Equal("succeeded", manifest.Status);
        Assert.Equal("stdio", manifest.Backend);
        Assert.Equal(result.Metrics.NormalizedVoxelHash + Environment.NewLine,
            File.ReadAllText(Path.Combine(result.RunDirectory, "outputs", "voxel-hash.txt")));
        Assert.Equal(result.Manifest.FinalModelSha256, result.Metrics.FinalModelSha256);
    }

    [Fact]
    public void ComparisonService_WritesJsonMarkdownAndCliOutputFromFixtureArtifacts()
    {
        using var temp = new TemporaryDirectory();
        CreateInputFiles(temp.RootPath);
        BenchmarkRunPlan plan = CreatePlan(Path.Combine(temp.RootPath, "artifacts"), "mcp-tool-loop");
        var writer = new BenchmarkArtifactWriter();
        BenchmarkSuiteArtifactContext suite = writer.CreateSuite(plan, FixedTimestamp);
        writer.WriteRunArtifacts(suite, CreateArtifactRequest(plan.Runs[0], "succeeded"), temp.RootPath);
        writer.WriteRunArtifacts(suite, CreateArtifactRequest(plan.Runs[1], "failed"), temp.RootPath);

        var output = new StringWriter();
        var error = new StringWriter();
        int exitCode = new BenchmarkCli().Execute(["compare", suite.RootPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Wrote comparison for 2 runs", output.ToString(), StringComparison.Ordinal);

        string comparisonJsonPath = Path.Combine(suite.RootPath, "comparison.json");
        string comparisonMarkdownPath = Path.Combine(suite.RootPath, "comparison.md");
        Assert.True(File.Exists(comparisonJsonPath));
        Assert.True(File.Exists(comparisonMarkdownPath));

        BenchmarkComparisonReport report = ReadJson<BenchmarkComparisonReport>(comparisonJsonPath);
        Assert.Equal("primitive-builds", report.SuiteId);
        Assert.Equal(2, report.RunCount);
        Assert.Contains(report.Entries, entry => entry.Status == "succeeded" && entry.VoxelCount == 3);
        Assert.Contains(report.Entries, entry => entry.Status == "failed" && entry.FailureMessage == "fixture failure");
        Assert.Contains(report.Entries, entry => entry.PaletteUsage.Count > 0);
        Assert.Contains(report.Entries, entry => entry.Artifacts.FinalVforge.EndsWith("outputs/final.vforge", StringComparison.Ordinal));

        string markdown = File.ReadAllText(comparisonMarkdownPath);
        Assert.Contains("# Benchmark comparison: primitive-builds", markdown, StringComparison.Ordinal);
        Assert.Contains("| simple-chair | baseline-tools | 1 | succeeded | 3 |", markdown, StringComparison.Ordinal);
        Assert.Contains("| simple-chair | primitive-tools | 1 | failed | 3 |", markdown, StringComparison.Ordinal);
        Assert.Contains("1:red=2", markdown, StringComparison.Ordinal);
        Assert.Contains("Failed runs", markdown, StringComparison.Ordinal);
        Assert.Contains("fixture failure", markdown, StringComparison.Ordinal);
        Assert.Contains("[final]", markdown, StringComparison.Ordinal);
    }

    private static BenchmarkRunPlan CreatePlan(string artifactRoot, string backend)
    {
        return new BenchmarkRunPlan
        {
            SuiteId = "primitive-builds",
            ArtifactRoot = artifactRoot,
            Backend = backend,
            FailFast = false,
            Runs =
            [
                CreatePlannedRun("baseline-tools"),
                CreatePlannedRun("primitive-tools"),
            ],
        };
    }

    private static BenchmarkPlannedRun CreatePlannedRun(string variantId)
    {
        return new BenchmarkPlannedRun
        {
            CaseId = "simple-chair",
            VariantId = variantId,
            Trial = 1,
            Provider = "configured-chat-client",
            Model = "local-model",
            PromptFile = "benchmarks/prompts/simple-chair.md",
            SystemPromptFile = "benchmarks/prompts/system.md",
            InitialModel = "benchmarks/models/initial.vforge",
            PaletteFile = "benchmarks/palettes/basic.json",
            ToolPreset = "mcp-authoring-v1",
        };
    }

    private static BenchmarkRunArtifactRequest CreateArtifactRequest(BenchmarkPlannedRun run, string status)
    {
        return new BenchmarkRunArtifactRequest
        {
            PlannedRun = run,
            Model = CreatePopulatedModel(),
            Labels = CreateLabels([new Point3(0, 0, 0), new Point3(1, 0, 0)]),
            Metadata = new ProjectMetadata { Name = "fixture" },
            StartedAtUtc = FixedTimestamp,
            EndedAtUtc = FixedTimestamp.AddSeconds(3),
            Status = status,
            ToolCallCount = 5,
            FailedToolCallCount = string.Equals(status, "failed", StringComparison.Ordinal) ? 1 : 0,
            UndoableMutationCount = 3,
            LlmRounds = 2,
            ErrorCount = string.Equals(status, "failed", StringComparison.Ordinal) ? 1 : 0,
            MaxRounds = 12,
            GitCommit = "abcdef",
            Failure = string.Equals(status, "failed", StringComparison.Ordinal)
                ? new BenchmarkFailureArtifact
                {
                    Phase = "fixture",
                    ExceptionType = "FixtureException",
                    Message = "fixture failure",
                }
                : null,
        };
    }

    private static VoxelModel CreatePopulatedModel()
    {
        VoxelModel model = CreateModel();
        model.GridHint = 16;
        model.Palette.Set(1, new MaterialDef { Name = "red", Color = new RgbaColor(255, 0, 0) });
        model.Palette.Set(2, new MaterialDef { Name = "blue", Color = new RgbaColor(0, 0, 255) });
        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        model.SetVoxel(new Point3(4, 0, 0), 2);
        return model;
    }

    private static VoxelModel CreateModel()
    {
        return new VoxelModel(NullLogger<VoxelModel>.Instance);
    }

    private static LabelIndex CreateLabels(IReadOnlyList<Point3> voxels)
    {
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        labels.AddOrUpdateRegion(new RegionDef { Id = new RegionId("seat"), Name = "Seat" });
        labels.AssignRegion(new RegionId("seat"), voxels);
        return labels;
    }

    private static void CreateInputFiles(string root)
    {
        WriteFile(Path.Combine(root, "benchmarks", "prompts", "simple-chair.md"), "Build a chair.");
        WriteFile(Path.Combine(root, "benchmarks", "prompts", "system.md"), "Use voxel tools.");
        WriteFile(Path.Combine(root, "benchmarks", "palettes", "basic.json"), "{\"palette\":[]}");
        WriteFile(Path.Combine(root, "benchmarks", "models", "initial.vforge"), "{\"formatVersion\":1}");
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static T ReadJson<T>(string path)
    {
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
        }) ?? throw new InvalidOperationException($"Failed to read JSON: {path}");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            RootPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "voxelforge-eval-artifacts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
