using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkProviderManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("seed")]
    public int? Seed { get; init; }
}

public sealed class BenchmarkRunManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("suite_id")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("case_id")]
    public required string CaseId { get; init; }

    [JsonPropertyName("variant_id")]
    public required string VariantId { get; init; }

    [JsonPropertyName("trial")]
    public int Trial { get; init; }

    [JsonPropertyName("started_at_utc")]
    public required DateTimeOffset StartedAtUtc { get; init; }

    [JsonPropertyName("ended_at_utc")]
    public required DateTimeOffset EndedAtUtc { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("backend")]
    public required string Backend { get; init; }

    [JsonPropertyName("git_commit")]
    public string? GitCommit { get; init; }

    [JsonPropertyName("working_tree_dirty")]
    public bool WorkingTreeDirty { get; init; }

    [JsonPropertyName("provider")]
    public required BenchmarkProviderManifest Provider { get; init; }

    [JsonPropertyName("tool_preset")]
    public string? ToolPreset { get; init; }

    [JsonPropertyName("prompt_sha256")]
    public string? PromptSha256 { get; init; }

    [JsonPropertyName("system_prompt_sha256")]
    public string? SystemPromptSha256 { get; init; }

    [JsonPropertyName("tool_schema_sha256")]
    public string? ToolSchemaSha256 { get; init; }

    [JsonPropertyName("initial_model_sha256")]
    public string? InitialModelSha256 { get; init; }

    [JsonPropertyName("final_model_sha256")]
    public required string FinalModelSha256 { get; init; }

    [JsonPropertyName("transcript_sha256")]
    public string? TranscriptSha256 { get; init; }

    [JsonPropertyName("metrics_sha256")]
    public required string MetricsSha256 { get; init; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; init; }

    [JsonPropertyName("max_rounds")]
    public int? MaxRounds { get; init; }

    [JsonPropertyName("llm_rounds")]
    public int LlmRounds { get; init; }

    [JsonPropertyName("tool_call_count")]
    public int ToolCallCount { get; init; }

    [JsonPropertyName("error_count")]
    public int ErrorCount { get; init; }
}

public sealed class BenchmarkSuiteManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("suite_id")]
    public required string SuiteId { get; init; }

    [JsonPropertyName("created_at_utc")]
    public required DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("backend")]
    public required string Backend { get; init; }

    [JsonPropertyName("run_count")]
    public int RunCount { get; init; }
}

public sealed class BenchmarkFailureArtifact
{
    [JsonPropertyName("phase")]
    public required string Phase { get; init; }

    [JsonPropertyName("exception_type")]
    public string? ExceptionType { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public sealed class BenchmarkRunArtifactRequest
{
    public required BenchmarkPlannedRun PlannedRun { get; init; }
    public required VoxelModel Model { get; init; }
    public required LabelIndex Labels { get; init; }
    public IReadOnlyList<AnimationClip> Clips { get; init; } = [];
    public ProjectMetadata Metadata { get; init; } = new();
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset EndedAtUtc { get; init; }
    public string Status { get; init; } = "succeeded";
    public int ToolCallCount { get; init; }
    public int FailedToolCallCount { get; init; }
    public int UndoableMutationCount { get; init; }
    public int LlmRounds { get; init; }
    public int ErrorCount { get; init; }
    public int? MaxRounds { get; init; }
    public string? GitCommit { get; init; }
    public bool WorkingTreeDirty { get; init; }
    public double? Temperature { get; init; }
    public int? Seed { get; init; }
    public string? ToolSchemaSha256 { get; init; }
    public string? ConversationTranscriptJsonl { get; init; }
    public string? ToolCallsTranscriptJsonl { get; init; }
    public string? StdioTranscriptJsonl { get; init; }
    public string? StdoutLog { get; init; }
    public string? StderrLog { get; init; }
    public BenchmarkFailureArtifact? Failure { get; init; }
}

public sealed class BenchmarkSuiteArtifactContext
{
    public required string SuiteId { get; init; }
    public required string RootPath { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required string Backend { get; init; }
}

public sealed class BenchmarkRunArtifactResult
{
    public required string RunDirectory { get; init; }
    public required BenchmarkRunManifest Manifest { get; init; }
    public required BenchmarkModelMetrics Metrics { get; init; }
}

public sealed class BenchmarkArtifactWriter
{
    private readonly BenchmarkMetricsService _metricsService;

    public BenchmarkArtifactWriter()
        : this(new BenchmarkMetricsService())
    {
    }

    public BenchmarkArtifactWriter(BenchmarkMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    public BenchmarkSuiteArtifactContext CreateSuite(BenchmarkRunPlan plan, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string suiteRoot = Path.Combine(
            plan.ArtifactRoot,
            plan.SuiteId,
            CreateTimestampDirectoryName(createdAtUtc));
        string uniqueSuiteRoot = CreateUniqueDirectory(suiteRoot);

        var manifest = new BenchmarkSuiteManifest
        {
            SuiteId = plan.SuiteId,
            CreatedAtUtc = createdAtUtc,
            Backend = plan.Backend,
            RunCount = plan.Runs.Count,
        };
        WriteJson(Path.Combine(uniqueSuiteRoot, "suite-manifest.json"), manifest);

        return new BenchmarkSuiteArtifactContext
        {
            SuiteId = plan.SuiteId,
            RootPath = uniqueSuiteRoot,
            CreatedAtUtc = createdAtUtc,
            Backend = plan.Backend,
        };
    }

    public BenchmarkRunArtifactResult WriteRunArtifacts(
        BenchmarkSuiteArtifactContext suite,
        BenchmarkRunArtifactRequest request,
        string inputRoot)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRoot);

        BenchmarkPlannedRun run = request.PlannedRun;
        string runDirectory = Path.Combine(
            suite.RootPath,
            run.CaseId,
            run.VariantId,
            FormattableString.Invariant($"trial-{run.Trial}"));
        CreateEmptyDirectory(runDirectory);

        string inputsDirectory = Path.Combine(runDirectory, "inputs");
        string transcriptsDirectory = Path.Combine(runDirectory, "transcripts");
        string outputsDirectory = Path.Combine(runDirectory, "outputs");
        string reportsDirectory = Path.Combine(runDirectory, "reports");
        Directory.CreateDirectory(inputsDirectory);
        Directory.CreateDirectory(transcriptsDirectory);
        Directory.CreateDirectory(outputsDirectory);
        Directory.CreateDirectory(reportsDirectory);

        string? promptSha = CopyInputFile(inputRoot, run.PromptFile, Path.Combine(inputsDirectory, "prompt.md"));
        string? systemPromptSha = CopyOptionalInputFile(inputRoot, run.SystemPromptFile, Path.Combine(inputsDirectory, "system-prompt.md"));
        string? initialSha = CopyOptionalInputFile(inputRoot, run.InitialModel, Path.Combine(inputsDirectory, "initial.vforge"));
        CopyOptionalInputFile(inputRoot, run.PaletteFile, Path.Combine(inputsDirectory, "palette.json"));
        WriteJson(Path.Combine(inputsDirectory, "runset-fragment.json"), new BenchmarkRunsetFragment(run));

        WriteTranscripts(transcriptsDirectory, suite.Backend, request);

        string finalProjectJson = new ProjectSerializer(NullLoggerFactory.Instance).Serialize(
            request.Model,
            request.Labels,
            request.Clips,
            request.Metadata);
        string finalModelPath = Path.Combine(outputsDirectory, "final.vforge");
        File.WriteAllText(finalModelPath, finalProjectJson);
        string finalModelSha = _metricsService.ComputeFileSha256(finalModelPath);

        BenchmarkModelMetrics metrics = _metricsService.Compute(
            request.Model,
            request.Labels,
            request.Clips,
            new BenchmarkMetricsOptions
            {
                ToolCallCount = request.ToolCallCount,
                FailedToolCallCount = request.FailedToolCallCount,
                UndoableMutationCount = request.UndoableMutationCount,
                FinalModelSha256 = finalModelSha,
            });

        WriteJson(Path.Combine(outputsDirectory, "model-info.json"), CreateModelInfo(metrics));
        string metricsPath = Path.Combine(outputsDirectory, "metrics.json");
        WriteJson(metricsPath, metrics);
        File.WriteAllText(Path.Combine(outputsDirectory, "voxel-hash.txt"), metrics.NormalizedVoxelHash + Environment.NewLine);

        if (request.Failure is not null)
            WriteJson(Path.Combine(outputsDirectory, "failure.json"), request.Failure);

        string metricsSha = _metricsService.ComputeFileSha256(metricsPath);
        string transcriptSha = ComputeTranscriptSha(transcriptsDirectory);

        var manifest = new BenchmarkRunManifest
        {
            SuiteId = suite.SuiteId,
            CaseId = run.CaseId,
            VariantId = run.VariantId,
            Trial = run.Trial,
            StartedAtUtc = request.StartedAtUtc,
            EndedAtUtc = request.EndedAtUtc,
            Status = request.Status,
            Backend = suite.Backend,
            GitCommit = request.GitCommit,
            WorkingTreeDirty = request.WorkingTreeDirty,
            Provider = new BenchmarkProviderManifest
            {
                Name = run.Provider,
                Model = run.Model,
                Temperature = request.Temperature,
                Seed = request.Seed,
            },
            ToolPreset = run.ToolPreset,
            PromptSha256 = promptSha,
            SystemPromptSha256 = systemPromptSha,
            ToolSchemaSha256 = request.ToolSchemaSha256,
            InitialModelSha256 = initialSha,
            FinalModelSha256 = finalModelSha,
            TranscriptSha256 = transcriptSha,
            MetricsSha256 = metricsSha,
            ElapsedMs = (long)Math.Max(0, (request.EndedAtUtc - request.StartedAtUtc).TotalMilliseconds),
            MaxRounds = request.MaxRounds,
            LlmRounds = request.LlmRounds,
            ToolCallCount = request.ToolCallCount,
            ErrorCount = request.ErrorCount,
        };
        WriteJson(Path.Combine(runDirectory, "run-manifest.json"), manifest);
        WriteRunSummary(Path.Combine(reportsDirectory, "run-summary.md"), manifest, metrics);

        return new BenchmarkRunArtifactResult
        {
            RunDirectory = runDirectory,
            Manifest = manifest,
            Metrics = metrics,
        };
    }

    private string? CopyOptionalInputFile(string inputRoot, string? sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        return CopyInputFile(inputRoot, sourcePath, targetPath);
    }

    private string CopyInputFile(string inputRoot, string sourcePath, string targetPath)
    {
        string fullSourcePath = Path.Combine(inputRoot, sourcePath);
        File.Copy(fullSourcePath, targetPath, overwrite: false);
        return _metricsService.ComputeFileSha256(targetPath);
    }

    private static void WriteTranscripts(string transcriptsDirectory, string backend, BenchmarkRunArtifactRequest request)
    {
        WriteText(Path.Combine(transcriptsDirectory, "conversation.jsonl"), request.ConversationTranscriptJsonl);
        WriteText(Path.Combine(transcriptsDirectory, "tool-calls.jsonl"), request.ToolCallsTranscriptJsonl);
        if (string.Equals(backend, "stdio", StringComparison.Ordinal) || request.StdioTranscriptJsonl is not null)
            WriteText(Path.Combine(transcriptsDirectory, "stdio.jsonl"), request.StdioTranscriptJsonl);
        WriteText(Path.Combine(transcriptsDirectory, "stdout.log"), request.StdoutLog);
        WriteText(Path.Combine(transcriptsDirectory, "stderr.log"), request.StderrLog);
    }

    private static void WriteText(string path, string? value)
    {
        File.WriteAllText(path, value ?? string.Empty);
    }

    private static object CreateModelInfo(BenchmarkModelMetrics metrics)
    {
        return new
        {
            schema_version = 1,
            voxel_count = metrics.VoxelCount,
            grid_hint = metrics.GridHint,
            bounds_min = metrics.BoundsMin,
            bounds_max = metrics.BoundsMax,
            palette_usage = metrics.PaletteUsage,
            region_count = metrics.RegionCount,
            labeled_voxel_count = metrics.LabeledVoxelCount,
            animation_clip_count = metrics.AnimationClipCount,
        };
    }

    private static void WriteRunSummary(string path, BenchmarkRunManifest manifest, BenchmarkModelMetrics metrics)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine($"# Benchmark run: {manifest.CaseId} / {manifest.VariantId} / trial {manifest.Trial}");
        writer.WriteLine();
        writer.WriteLine($"- Status: {manifest.Status}");
        writer.WriteLine($"- Backend: {manifest.Backend}");
        writer.WriteLine($"- Voxels: {metrics.VoxelCount}");
        writer.WriteLine($"- Bounds: {FormatBounds(metrics)}");
        writer.WriteLine($"- Tool calls: {metrics.ToolCallCount}");
        writer.WriteLine($"- Failed tool calls: {metrics.FailedToolCallCount}");
        writer.WriteLine($"- Normalized voxel hash: {metrics.NormalizedVoxelHash}");
    }

    private static string FormatBounds(BenchmarkModelMetrics metrics)
    {
        return metrics.BoundsMin is null || metrics.BoundsMax is null
            ? "empty"
            : $"{metrics.BoundsMin}..{metrics.BoundsMax}";
    }

    private string ComputeTranscriptSha(string transcriptsDirectory)
    {
        var builder = new StringBuilder();
        string[] files = Directory.GetFiles(transcriptsDirectory).OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < files.Length; i++)
        {
            builder.Append(Path.GetFileName(files[i]));
            builder.Append('\n');
            builder.Append(File.ReadAllText(files[i]));
            builder.Append('\n');
        }

        return _metricsService.ComputeTextSha256(builder.ToString());
    }

    private static string CreateTimestampDirectoryName(DateTimeOffset timestamp)
    {
        return timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateUniqueDirectory(string path)
    {
        for (int suffix = 0; suffix < 1000; suffix++)
        {
            string candidate = suffix == 0
                ? path
                : FormattableString.Invariant($"{path}-{suffix:000}");

            if (Directory.Exists(candidate))
            {
                if (!Directory.EnumerateFileSystemEntries(candidate).Any())
                    return candidate;

                continue;
            }

            Directory.CreateDirectory(candidate);
            return candidate;
        }

        throw new IOException($"Could not create a unique artifact directory for {path}.");
    }

    private static void CreateEmptyDirectory(string path)
    {
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            throw new IOException($"Run artifact directory already exists and is not empty: {path}");

        Directory.CreateDirectory(path);
    }

    private static void WriteJson<T>(string path, T value)
    {
        string json = JsonSerializer.Serialize(value, BenchmarkJson.WriteIndentedOptions);
        File.WriteAllText(path, json + Environment.NewLine);
    }

    private sealed class BenchmarkRunsetFragment
    {
        public BenchmarkRunsetFragment(BenchmarkPlannedRun run)
        {
            CaseId = run.CaseId;
            VariantId = run.VariantId;
            Trial = run.Trial;
            Provider = run.Provider;
            Model = run.Model;
            PromptFile = run.PromptFile;
            SystemPromptFile = run.SystemPromptFile;
            InitialModel = run.InitialModel;
            PaletteFile = run.PaletteFile;
            ToolPreset = run.ToolPreset;
            Temperature = run.Temperature;
            Seed = run.Seed;
        }

        [JsonPropertyName("case_id")]
        public string CaseId { get; }

        [JsonPropertyName("variant_id")]
        public string VariantId { get; }

        [JsonPropertyName("trial")]
        public int Trial { get; }

        [JsonPropertyName("provider")]
        public string Provider { get; }

        [JsonPropertyName("model")]
        public string Model { get; }

        [JsonPropertyName("prompt_file")]
        public string PromptFile { get; }

        [JsonPropertyName("system_prompt_file")]
        public string? SystemPromptFile { get; }

        [JsonPropertyName("initial_model")]
        public string? InitialModel { get; }

        [JsonPropertyName("palette_file")]
        public string? PaletteFile { get; }

        [JsonPropertyName("tool_preset")]
        public string? ToolPreset { get; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; }

        [JsonPropertyName("seed")]
        public int? Seed { get; }
    }
}
