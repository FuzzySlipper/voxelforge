using System.Text.Json;

namespace VoxelForge.Import;

public sealed class ImportCli
{
    private readonly ImportPlanParser _parser;

    public ImportCli()
        : this(new ImportPlanParser(new ImportPlanValidator()))
    {
    }

    public ImportCli(ImportPlanParser parser)
    {
        _parser = parser;
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
        if (string.Equals(command, "replay", StringComparison.Ordinal)
            || string.Equals(command, "import", StringComparison.Ordinal))
        {
            error.WriteLine("Replay/materialization is not implemented in task 863. Use normalize or validate.");
            return 2;
        }

        if (!string.Equals(command, "normalize", StringComparison.Ordinal)
            && !string.Equals(command, "validate", StringComparison.Ordinal))
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

        ImportNormalizeResult result = _parser.NormalizeFile(parseResult.InputPath!, new ImportNormalizeOptions
        {
            Format = parseResult.Format,
            ToolName = parseResult.ToolName,
            Strict = parseResult.Strict,
            MaxOperations = parseResult.MaxOperations,
            MaxGeneratedVoxels = parseResult.MaxGeneratedVoxels,
        });

        if (!string.IsNullOrWhiteSpace(parseResult.ReportOutPath))
            File.WriteAllText(parseResult.ReportOutPath, JsonSerializer.Serialize(result.Report, ImportJson.SerializerOptions));

        if (string.Equals(command, "normalize", StringComparison.Ordinal))
        {
            if (!result.Success || result.Plan is null)
            {
                WriteDiagnostics(error, result.Diagnostics);
                return 1;
            }

            string planJson = JsonSerializer.Serialize(result.Plan, ImportJson.SerializerOptions);
            if (!string.IsNullOrWhiteSpace(parseResult.PlanOutPath))
            {
                File.WriteAllText(parseResult.PlanOutPath, planJson);
                output.WriteLine($"Wrote import plan: {parseResult.PlanOutPath}");
            }
            else
            {
                output.WriteLine(planJson);
            }

            WriteSummary(output, result.Report);
            return 0;
        }

        WriteSummary(result.Report.ErrorCount == 0 ? output : error, result.Report);
        if (result.Diagnostics.Count > 0)
            WriteDiagnostics(result.Report.ErrorCount == 0 ? output : error, result.Diagnostics);

        return result.Report.ErrorCount == 0 ? 0 : 1;
    }

    private static CliParseResult ParseOptions(string[] args)
    {
        if (args.Length < 2)
            return CliParseResult.Failure("Input path is required.");

        var result = new CliParseResult
        {
            Success = true,
            InputPath = args[1],
            Format = ImportInputFormat.Auto,
            Strict = true,
            MaxOperations = 10000,
            MaxGeneratedVoxels = 65536,
        };

        int index = 2;
        while (index < args.Length)
        {
            string arg = args[index];
            if (!RequiresValue(arg))
                return CliParseResult.Failure($"Unknown option '{arg}'.");

            if (index + 1 >= args.Length)
                return CliParseResult.Failure($"{arg} requires a value.");

            string value = args[index + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
                return CliParseResult.Failure($"{arg} requires a value.");

            ApplyValue(result, arg, value);
            if (!result.Success)
                return result;

            index += 2;
        }

        return result;
    }

    private static bool RequiresValue(string arg)
    {
        return string.Equals(arg, "--format", StringComparison.Ordinal)
            || string.Equals(arg, "--tool", StringComparison.Ordinal)
            || string.Equals(arg, "--strict", StringComparison.Ordinal)
            || string.Equals(arg, "--max-operations", StringComparison.Ordinal)
            || string.Equals(arg, "--max-generated-voxels", StringComparison.Ordinal)
            || string.Equals(arg, "--out", StringComparison.Ordinal)
            || string.Equals(arg, "--plan-out", StringComparison.Ordinal)
            || string.Equals(arg, "--report-out", StringComparison.Ordinal);
    }

    private static void ApplyValue(CliParseResult result, string arg, string value)
    {
        if (string.Equals(arg, "--format", StringComparison.Ordinal))
        {
            if (!TryParseFormat(value, out ImportInputFormat format))
            {
                result.Success = false;
                result.Message = $"Unsupported --format '{value}'.";
                return;
            }

            result.Format = format;
            return;
        }

        if (string.Equals(arg, "--tool", StringComparison.Ordinal))
        {
            result.ToolName = value;
            return;
        }

        if (string.Equals(arg, "--strict", StringComparison.Ordinal))
        {
            if (!bool.TryParse(value, out bool strict))
            {
                result.Success = false;
                result.Message = "--strict must be true or false.";
                return;
            }

            result.Strict = strict;
            return;
        }

        if (string.Equals(arg, "--max-operations", StringComparison.Ordinal))
        {
            if (!int.TryParse(value, out int maxOperations))
            {
                result.Success = false;
                result.Message = "--max-operations must be an integer.";
                return;
            }

            result.MaxOperations = maxOperations;
            return;
        }

        if (string.Equals(arg, "--max-generated-voxels", StringComparison.Ordinal))
        {
            if (!int.TryParse(value, out int maxGeneratedVoxels))
            {
                result.Success = false;
                result.Message = "--max-generated-voxels must be an integer.";
                return;
            }

            result.MaxGeneratedVoxels = maxGeneratedVoxels;
            return;
        }

        if (string.Equals(arg, "--out", StringComparison.Ordinal)
            || string.Equals(arg, "--plan-out", StringComparison.Ordinal))
        {
            result.PlanOutPath = value;
            return;
        }

        if (string.Equals(arg, "--report-out", StringComparison.Ordinal))
            result.ReportOutPath = value;
    }

    private static bool TryParseFormat(string value, out ImportInputFormat format)
    {
        format = value switch
        {
            "auto" => ImportInputFormat.Auto,
            "tool-envelope" => ImportInputFormat.ToolEnvelope,
            "raw-arguments" => ImportInputFormat.RawArguments,
            "tool-call-array" => ImportInputFormat.ToolCallArray,
            "tool-calls-jsonl" => ImportInputFormat.ToolCallsJsonl,
            "stdio-jsonl" => ImportInputFormat.StdioJsonl,
            _ => ImportInputFormat.Auto,
        };

        return value == "auto"
            || value == "tool-envelope"
            || value == "raw-arguments"
            || value == "tool-call-array"
            || value == "tool-calls-jsonl"
            || value == "stdio-jsonl";
    }

    private static bool IsHelp(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.Ordinal)
            || string.Equals(arg, "-h", StringComparison.Ordinal)
            || string.Equals(arg, "help", StringComparison.Ordinal);
    }

    private static void WriteSummary(TextWriter writer, ImportReport report)
    {
        writer.WriteLine($"Status: {report.Status}");
        writer.WriteLine($"Source format: {report.SourceFormat}");
        writer.WriteLine($"Operations: {report.OperationCount}");
        writer.WriteLine($"Errors: {report.ErrorCount}");
        writer.WriteLine($"Warnings: {report.WarningCount}");
    }

    private static void WriteDiagnostics(TextWriter writer, IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        for (int i = 0; i < diagnostics.Count; i++)
        {
            ImportDiagnostic diagnostic = diagnostics[i];
            writer.Write(diagnostic.Severity == ImportDiagnosticSeverity.Error ? "error" : "warning");
            writer.Write(' ');
            writer.Write(diagnostic.Code);
            if (!string.IsNullOrWhiteSpace(diagnostic.SourcePath))
            {
                writer.Write(" at ");
                writer.Write(diagnostic.SourcePath);
            }

            if (diagnostic.Line.HasValue)
            {
                writer.Write(':');
                writer.Write(diagnostic.Line.Value);
            }

            if (diagnostic.OperationIndex.HasValue)
            {
                writer.Write(" op ");
                writer.Write(diagnostic.OperationIndex.Value);
            }

            if (!string.IsNullOrWhiteSpace(diagnostic.JsonPointer))
            {
                writer.Write(' ');
                writer.Write(diagnostic.JsonPointer);
            }

            writer.Write(": ");
            writer.WriteLine(diagnostic.Message);
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("VoxelForge.Import");
        writer.WriteLine("Usage:");
        writer.WriteLine("  normalize <input> --format <auto|tool-envelope|raw-arguments|tool-call-array|tool-calls-jsonl|stdio-jsonl> [--tool <tool>] [--out <plan.json>] [--report-out <report.json>]");
        writer.WriteLine("  validate <input> --format <auto|tool-envelope|raw-arguments|tool-call-array|tool-calls-jsonl|stdio-jsonl> [--tool <tool>] [--report-out <report.json>]");
        writer.WriteLine("Replay/materialization commands are intentionally deferred beyond task 863.");
    }

    private sealed class CliParseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? InputPath { get; init; }
        public ImportInputFormat Format { get; set; }
        public string? ToolName { get; set; }
        public bool Strict { get; set; }
        public int MaxOperations { get; set; }
        public int MaxGeneratedVoxels { get; set; }
        public string? PlanOutPath { get; set; }
        public string? ReportOutPath { get; set; }

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
