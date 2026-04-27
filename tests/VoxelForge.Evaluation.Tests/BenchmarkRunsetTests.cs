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
    public void Cli_RunWithoutDryRunReturnsNotImplementedError()
    {
        using var temp = new TemporaryRunset(SampleRunsetJson());
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new BenchmarkCli().Execute(["run", temp.Path], output, error);

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Live benchmark execution is not implemented", error.ToString(), StringComparison.Ordinal);
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

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
