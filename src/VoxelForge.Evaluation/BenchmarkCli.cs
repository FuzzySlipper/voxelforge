using System.Text.Json;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkCli
{
    private readonly BenchmarkRunsetLoader _loader;
    private readonly BenchmarkPlanner _planner;
    private readonly BenchmarkPlanWriter _planWriter;
    private readonly BenchmarkComparisonService _comparisonService;
    private readonly BenchmarkRunner _runner;

    public BenchmarkCli()
        : this(
            new BenchmarkRunsetLoader(new BenchmarkRunsetValidator()),
            new BenchmarkPlanner(new BenchmarkRunsetValidator()),
            new BenchmarkPlanWriter(),
            new BenchmarkComparisonService(),
            new BenchmarkRunner())
    {
    }

    public BenchmarkCli(
        BenchmarkRunsetLoader loader,
        BenchmarkPlanner planner,
        BenchmarkPlanWriter planWriter,
        BenchmarkComparisonService comparisonService,
        BenchmarkRunner runner)
    {
        _loader = loader;
        _planner = planner;
        _planWriter = planWriter;
        _comparisonService = comparisonService;
        _runner = runner;
    }

    public int Execute(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteUsage(output);
            return 0;
        }

        string command = args[0];
        if (string.Equals(command, "compare", StringComparison.Ordinal))
        {
            if (args.Length != 2)
            {
                error.WriteLine("Suite artifact directory is required.");
                return 2;
            }

            try
            {
                BenchmarkComparisonReport report = _comparisonService.CompareAndWrite(args[1]);
                output.WriteLine($"Wrote comparison for {report.RunCount} runs: {args[1]}");
                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                error.WriteLine($"Comparison failed: {ex.Message}");
                return 1;
            }
        }

        if (!string.Equals(command, "plan", StringComparison.Ordinal)
            && !string.Equals(command, "run", StringComparison.Ordinal))
        {
            error.WriteLine($"Unknown command '{command}'.");
            WriteUsage(error);
            return 2;
        }

        CliParseResult parseResult = ParseOptions(args);
        if (!parseResult.Success)
        {
            error.WriteLine(parseResult.Message);
            return 2;
        }

        BenchmarkRunsetLoadResult loadResult = _loader.Load(parseResult.RunsetPath!);
        if (!loadResult.Success || loadResult.Runset is null)
        {
            WriteDiagnostics(error, loadResult.Diagnostics);
            return 1;
        }

        BenchmarkPlanResult planResult = _planner.BuildPlan(loadResult.Runset, new BenchmarkPlanOptions
        {
            CaseId = parseResult.CaseId,
            VariantId = parseResult.VariantId,
            TrialsOverride = parseResult.TrialsOverride,
            ArtifactRootOverride = parseResult.ArtifactRootOverride,
            Backend = parseResult.Backend,
            FailFast = parseResult.FailFast,
        });

        if (!planResult.Success || planResult.Plan is null)
        {
            WriteDiagnostics(error, planResult.Diagnostics);
            return 1;
        }

        if (parseResult.DryRun || string.Equals(command, "plan", StringComparison.Ordinal))
        {
            if (parseResult.DryRun)
                output.WriteLine("Dry run: no model execution will be performed.");

            _planWriter.Write(planResult.Plan, output);
            return 0;
        }

        try
        {
            string inputRoot = Path.GetDirectoryName(Path.GetFullPath(parseResult.RunsetPath!)) ?? Directory.GetCurrentDirectory();
            BenchmarkRunSuiteResult suiteResult = _runner.RunAsync(planResult.Plan, inputRoot).GetAwaiter().GetResult();
            output.WriteLine($"Wrote benchmark suite for {suiteResult.RunCount} runs: {suiteResult.SuiteDirectory}");
            if (suiteResult.FailureCount > 0)
                output.WriteLine($"Failed runs: {suiteResult.FailureCount}");
            return suiteResult.FailureCount == 0 ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            error.WriteLine($"Benchmark run failed: {ex.Message}");
            return 1;
        }
    }

    private static CliParseResult ParseOptions(string[] args)
    {
        if (args.Length < 2)
            return CliParseResult.Failure("Runset path is required.");

        var result = new CliParseResult
        {
            Success = true,
            RunsetPath = args[1],
            Backend = BenchmarkPlanner.DefaultBackend,
        };

        int index = 2;
        while (index < args.Length)
        {
            string arg = args[index];
            if (string.Equals(arg, "--dry-run", StringComparison.Ordinal))
            {
                result.DryRun = true;
                index++;
                continue;
            }

            if (string.Equals(arg, "--fail-fast", StringComparison.Ordinal))
            {
                result.FailFast = true;
                index++;
                continue;
            }

            if (RequiresValue(arg))
            {
                if (index + 1 >= args.Length)
                    return CliParseResult.Failure($"{arg} requires a value.");

                string value = args[index + 1];
                if (value.StartsWith("--", StringComparison.Ordinal))
                    return CliParseResult.Failure($"{arg} requires a value.");

                ApplyValue(result, arg, value);
                if (!result.Success)
                    return result;

                index += 2;
                continue;
            }

            return CliParseResult.Failure($"Unknown option '{arg}'.");
        }

        return result;
    }

    private static bool RequiresValue(string arg)
    {
        return string.Equals(arg, "--artifact-root", StringComparison.Ordinal)
            || string.Equals(arg, "--case", StringComparison.Ordinal)
            || string.Equals(arg, "--variant", StringComparison.Ordinal)
            || string.Equals(arg, "--trials", StringComparison.Ordinal)
            || string.Equals(arg, "--backend", StringComparison.Ordinal);
    }

    private static void ApplyValue(CliParseResult result, string arg, string value)
    {
        if (string.Equals(arg, "--artifact-root", StringComparison.Ordinal))
        {
            result.ArtifactRootOverride = value;
            return;
        }

        if (string.Equals(arg, "--case", StringComparison.Ordinal))
        {
            result.CaseId = value;
            return;
        }

        if (string.Equals(arg, "--variant", StringComparison.Ordinal))
        {
            result.VariantId = value;
            return;
        }

        if (string.Equals(arg, "--backend", StringComparison.Ordinal))
        {
            result.Backend = value;
            return;
        }

        if (string.Equals(arg, "--trials", StringComparison.Ordinal))
        {
            if (!int.TryParse(value, out int trials))
            {
                result.Success = false;
                result.Message = "--trials must be an integer.";
                return;
            }

            result.TrialsOverride = trials;
        }
    }

    private static bool IsHelp(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.Ordinal)
            || string.Equals(arg, "-h", StringComparison.Ordinal)
            || string.Equals(arg, "help", StringComparison.Ordinal);
    }

    private static void WriteDiagnostics(TextWriter writer, IReadOnlyList<BenchmarkDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            BenchmarkDiagnostic diagnostic = diagnostics[i];
            writer.Write(diagnostic.Severity);
            writer.Write(' ');
            writer.Write(diagnostic.Code);
            if (!string.IsNullOrWhiteSpace(diagnostic.Location))
            {
                writer.Write(" at ");
                writer.Write(diagnostic.Location);
            }

            writer.Write(": ");
            writer.WriteLine(diagnostic.Message);
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("VoxelForge.Evaluation");
        writer.WriteLine("Usage:");
        writer.WriteLine("  plan <runset.json> [--case <id>] [--variant <id>] [--trials <n>] [--backend <mcp-tool-loop|stdio>] [--artifact-root <dir>]");
        writer.WriteLine("  run <runset.json> [--dry-run] [--case <id>] [--variant <id>] [--trials <n>] [--backend <mcp-tool-loop|stdio>] [--artifact-root <dir>] [--fail-fast]");
        writer.WriteLine("  compare <suite-artifact-directory>");
    }

    private sealed class CliParseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? RunsetPath { get; init; }
        public string? CaseId { get; set; }
        public string? VariantId { get; set; }
        public int? TrialsOverride { get; set; }
        public string? ArtifactRootOverride { get; set; }
        public string Backend { get; set; } = BenchmarkPlanner.DefaultBackend;
        public bool DryRun { get; set; }
        public bool FailFast { get; set; }

        public static CliParseResult Failure(string message)
        {
            return new CliParseResult
            {
                Success = false,
                Message = message,
            };
        }
    }
}
