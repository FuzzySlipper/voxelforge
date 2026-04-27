using System.Text.Json;
using VoxelForge.Evaluation;

namespace VoxelForge.Evaluation.Tests;

public sealed class BenchmarkRunsetTests
{
    [Fact]
    public void Loader_ParsesValidRunsetAndPlannerBuildsFilteredMatrix()
    {
        using var temp = new TemporaryRunset(SampleRunsetJson());
        var loader = new BenchmarkRunsetLoader(new BenchmarkRunsetValidator());
        BenchmarkRunsetLoadResult loadResult = loader.Load(temp.Path);

        Assert.True(loadResult.Success, FormatDiagnostics(loadResult.Diagnostics));
        Assert.NotNull(loadResult.Runset);

        var planner = new BenchmarkPlanner(new BenchmarkRunsetValidator());
        BenchmarkPlanResult planResult = planner.BuildPlan(loadResult.Runset, new BenchmarkPlanOptions
        {
            CaseId = "simple-chair",
            VariantId = "primitive-tools",
            TrialsOverride = 2,
            ArtifactRootOverride = "artifacts/override",
            Backend = "stdio",
            FailFast = true,
        });

        Assert.True(planResult.Success, FormatDiagnostics(planResult.Diagnostics));
        Assert.NotNull(planResult.Plan);
        Assert.Equal("primitive-builds", planResult.Plan.SuiteId);
        Assert.Equal("artifacts/override", planResult.Plan.ArtifactRoot);
        Assert.Equal("stdio", planResult.Plan.Backend);
        Assert.True(planResult.Plan.FailFast);
        Assert.Equal(2, planResult.Plan.Runs.Count);
        Assert.All(planResult.Plan.Runs, run =>
        {
            Assert.Equal("simple-chair", run.CaseId);
            Assert.Equal("primitive-tools", run.VariantId);
            Assert.Equal("configured-chat-client", run.Provider);
            Assert.Equal("local-primitive", run.Model);
            Assert.Equal("benchmarks/prompts/voxel-authoring-with-primitives.md", run.SystemPromptFile);
            Assert.Equal("mcp-authoring-with-primitives-v1", run.ToolPreset);
        });
        Assert.Equal([1, 2], planResult.Plan.Runs.Select(run => run.Trial).ToArray());
    }

    [Fact]
    public void Validator_RejectsMissingRequiredFieldsUnsafePathsAndDuplicateIds()
    {
        var runset = new BenchmarkRunset
        {
            SchemaVersion = 1,
            SuiteId = "bad suite",
            ArtifactRoot = "../artifacts",
            Cases = [
                new BenchmarkCase
                {
                    CaseId = "case-one",
                    PromptFile = "/tmp/prompt.md",
                    InitialModel = "benchmarks/initial.txt",
                    PaletteFile = "benchmarks/../palette.json",
                },
                new BenchmarkCase
                {
                    CaseId = "case-one",
                    PromptFile = "benchmarks/prompts/prompt.md",
                },
            ],
            Variants = [
                new BenchmarkVariant
                {
                    VariantId = "variant/one",
                    Provider = "configured-chat-client",
                    Model = "model",
                    TopP = 2,
                },
            ],
        };

        BenchmarkValidationResult result = new BenchmarkRunsetValidator().Validate(runset);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "runset.suite_id.unsafe");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "runset.artifact_root.traversal");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "case.prompt_file.rooted");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "case.initial_model.extension");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "case.palette_file.traversal");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "case.case_id.duplicate");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "variant.variant_id.unsafe");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "variant.top_p");
    }

    [Fact]
    public void Planner_RejectsUnknownFiltersAndInvalidBackend()
    {
        var runset = ValidRunset();
        var planner = new BenchmarkPlanner(new BenchmarkRunsetValidator());

        BenchmarkPlanResult result = planner.BuildPlan(runset, new BenchmarkPlanOptions
        {
            CaseId = "missing-case",
            VariantId = "missing-variant",
            TrialsOverride = 0,
            Backend = "live-provider",
        });

        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "plan.backend.unsupported");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "plan.trials");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "plan.case_filter.not_found");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "plan.variant_filter.not_found");
    }

    [Fact]
    public void Cli_PlanPrintsResolvedMatrixWithoutExecutingModels()
    {
        using var temp = new TemporaryRunset(SampleRunsetJson());
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new BenchmarkCli().Execute([
            "plan",
            temp.Path,
            "--case",
            "simple-chair",
            "--variant",
            "baseline-tools",
            "--trials",
            "2",
            "--backend",
            "mcp-tool-loop",
        ], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string text = output.ToString();
        Assert.Contains("Suite: primitive-builds", text, StringComparison.Ordinal);
        Assert.Contains("Backend: mcp-tool-loop", text, StringComparison.Ordinal);
        Assert.Contains("Runs: 2", text, StringComparison.Ordinal);
        Assert.Contains("simple-chair | baseline-tools | trial 1", text, StringComparison.Ordinal);
        Assert.Contains("simple-chair | baseline-tools | trial 2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("execute", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_RunDryRunPrintsPlanAndDoesNotRequireLiveBackend()
    {
        using var temp = new TemporaryRunset(SampleRunsetJson());
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new BenchmarkCli().Execute([
            "run",
            temp.Path,
            "--dry-run",
            "--variant",
            "primitive-tools",
        ], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string text = output.ToString();
        Assert.Contains("Dry run: no model execution will be performed.", text, StringComparison.Ordinal);
        Assert.Contains("Runs: 1", text, StringComparison.Ordinal);
        Assert.Contains("simple-chair | primitive-tools | trial 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_RunWithoutConfiguredProviderWritesFailureArtifacts()
    {
        using var temp = new TemporaryRunset(SampleRunsetJson());
        CreateBenchmarkInputFiles(temp.RootPath);
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new BenchmarkCli().Execute(["run", temp.Path], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        string text = output.ToString();
        Assert.Contains("Wrote benchmark suite for 2 runs", text, StringComparison.Ordinal);
        Assert.Contains("Failed runs: 2", text, StringComparison.Ordinal);
        string suiteRoot = Assert.Single(Directory.GetDirectories(Path.Combine(temp.RootPath, "artifacts", "benchmarks", "primitive-builds")));
        Assert.True(File.Exists(Path.Combine(suiteRoot, "comparison.json")));
    }

    [Fact]
    public void Cli_RunMcpFakeProviderWritesTranscriptsAndFinalModel()
    {
        using var temp = new TemporaryRunset(FakeRunsetJson());
        WriteFile(Path.Combine(temp.RootPath, "prompts", "build.md"), "Build a small deterministic fixture.");
        WriteFile(Path.Combine(temp.RootPath, "prompts", "system.md"), "Use MCP tools.");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new BenchmarkCli().Execute(["run", temp.Path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("Wrote benchmark suite for 1 runs", output.ToString(), StringComparison.Ordinal);
        string suiteRoot = Assert.Single(Directory.GetDirectories(Path.Combine(temp.RootPath, "artifacts", "mcp", "mcp-fixture")));
        string runRoot = Path.Combine(suiteRoot, "fake-case", "fake-variant", "trial-1");
        Assert.True(File.Exists(Path.Combine(runRoot, "outputs", "final.vforge")));
        Assert.True(File.Exists(Path.Combine(runRoot, "transcripts", "conversation.jsonl")));
        Assert.True(File.Exists(Path.Combine(runRoot, "transcripts", "tool-calls.jsonl")));

        string toolCalls = File.ReadAllText(Path.Combine(runRoot, "transcripts", "tool-calls.jsonl"));
        Assert.Contains("\"name\":\"apply_voxel_primitives\"", toolCalls, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"save_model\"", toolCalls, StringComparison.Ordinal);

        string conversation = File.ReadAllText(Path.Combine(runRoot, "transcripts", "conversation.jsonl"));
        Assert.Contains("\"role\":\"system\"", conversation, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"user\"", conversation, StringComparison.Ordinal);
        Assert.Contains("\"role\":\"tool\"", conversation, StringComparison.Ordinal);

        string metricsJson = File.ReadAllText(Path.Combine(runRoot, "outputs", "metrics.json"));
        Assert.Contains("\"voxel_count\": 8", metricsJson, StringComparison.Ordinal);

        BenchmarkRunManifest manifest = JsonSerializer.Deserialize<BenchmarkRunManifest>(File.ReadAllText(Path.Combine(runRoot, "run-manifest.json")))
            ?? throw new InvalidOperationException("Failed to read manifest.");
        Assert.Equal("succeeded", manifest.Status);
        Assert.Equal("mcp-tool-loop", manifest.Backend);
        Assert.Equal(3, manifest.ToolCallCount);
        Assert.Equal(0, manifest.ErrorCount);
    }

    private static BenchmarkRunset ValidRunset()
    {
        return new BenchmarkRunset
        {
            SchemaVersion = 1,
            SuiteId = "primitive-builds",
            ArtifactRoot = "artifacts/benchmarks",
            Trials = 1,
            Cases = [
                new BenchmarkCase
                {
                    CaseId = "simple-chair",
                    PromptFile = "benchmarks/prompts/simple-chair.md",
                },
            ],
            Variants = [
                new BenchmarkVariant
                {
                    VariantId = "baseline-tools",
                    Provider = "configured-chat-client",
                    Model = "local-baseline",
                },
            ],
        };
    }

    private static string SampleRunsetJson()
    {
        return """
        {
          "schema_version": 1,
          "suite_id": "primitive-builds",
          "description": "Small voxel authoring prompts for high-level primitive tools.",
          "artifact_root": "artifacts/benchmarks",
          "max_rounds": 12,
          "trials": 1,
          "tool_preset": "mcp-authoring-v1",
          "cases": [
            {
              "case_id": "simple-chair",
              "prompt_file": "benchmarks/prompts/simple-chair.md",
              "system_prompt_file": "benchmarks/prompts/voxel-authoring-system.md",
              "initial_model": null,
              "palette_file": "benchmarks/palettes/basic-materials.json",
              "expected_tags": ["furniture", "symmetry"],
              "notes": "Should produce a seat, back, and four legs."
            }
          ],
          "variants": [
            {
              "variant_id": "baseline-tools",
              "provider": "configured-chat-client",
              "model": "local-baseline",
              "temperature": 0.2,
              "tool_preset": "mcp-authoring-v1"
            },
            {
              "variant_id": "primitive-tools",
              "provider": "configured-chat-client",
              "model": "local-primitive",
              "temperature": 0.2,
              "system_prompt_override": "benchmarks/prompts/voxel-authoring-with-primitives.md",
              "tool_preset": "mcp-authoring-with-primitives-v1"
            }
          ]
        }
        """;
    }

    private static string FakeRunsetJson()
    {
        return """
        {
          "schema_version": 1,
          "suite_id": "mcp-fixture",
          "artifact_root": "artifacts/mcp",
          "max_rounds": 4,
          "trials": 1,
          "tool_preset": "mcp-authoring-with-primitives-v1",
          "cases": [
            {
              "case_id": "fake-case",
              "prompt_file": "prompts/build.md",
              "system_prompt_file": "prompts/system.md"
            }
          ],
          "variants": [
            {
              "variant_id": "fake-variant",
              "provider": "fake",
              "model": "fake-primitive"
            }
          ]
        }
        """;
    }

    private static void CreateBenchmarkInputFiles(string root)
    {
        WriteFile(Path.Combine(root, "benchmarks", "prompts", "simple-chair.md"), "Build a chair.");
        WriteFile(Path.Combine(root, "benchmarks", "prompts", "voxel-authoring-system.md"), "Use voxel tools.");
        WriteFile(Path.Combine(root, "benchmarks", "prompts", "voxel-authoring-with-primitives.md"), "Use primitives.");
        WriteFile(Path.Combine(root, "benchmarks", "palettes", "basic-materials.json"), "[{\"index\":1,\"name\":\"red\",\"r\":255,\"g\":0,\"b\":0}]");
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string FormatDiagnostics(IReadOnlyList<BenchmarkDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
    }

    private sealed class TemporaryRunset : IDisposable
    {
        private readonly string _directory;

        public TemporaryRunset(string json)
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "voxelforge-eval-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "runset.json");
            File.WriteAllText(Path, json);
        }

        public string Path { get; }

        public string RootPath => _directory;

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
