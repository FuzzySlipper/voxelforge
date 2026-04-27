using System.Text.Json;

namespace VoxelForge.Import;

public sealed class ImportPlanParser
{
    private readonly ImportPlanValidator _validator;
    private readonly JsonDocumentOptions _documentOptions;

    public ImportPlanParser(ImportPlanValidator validator)
    {
        _validator = validator;
        _documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        };
    }

    public ImportNormalizeResult NormalizeFile(string path, ImportNormalizeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(path))
        {
            ImportDiagnostic diagnostic = CreateError("IMPORT000", $"Input file not found: {path}", path);
            return BuildResult(null, options, path, FormatToLabel(options.Format), [diagnostic]);
        }

        var diagnostics = new List<ImportDiagnostic>();
        var operations = new List<ImportPlanOperation>();
        string detectedFormat = FormatToLabel(options.Format);

        if (options.Format == ImportInputFormat.ToolCallsJsonl || options.Format == ImportInputFormat.StdioJsonl)
        {
            ParseJsonLinesFile(path, options, operations, diagnostics, ref detectedFormat);
        }
        else if (options.Format == ImportInputFormat.Auto && LooksLikeJsonLines(path))
        {
            ParseJsonLinesFile(path, options, operations, diagnostics, ref detectedFormat);
        }
        else
        {
            ParseJsonFile(path, options, operations, diagnostics, ref detectedFormat);
        }

        IReadOnlyList<ImportDiagnostic> validatedDiagnostics = _validator.ValidateOperations(
            operations,
            options,
            diagnostics,
            path);

        VoxelForgeImportPlan plan = CreatePlan(path, detectedFormat, options, operations);
        return BuildResult(plan, options, path, detectedFormat, validatedDiagnostics);
    }

    private void ParseJsonFile(
        string path,
        ImportNormalizeOptions options,
        List<ImportPlanOperation> operations,
        List<ImportDiagnostic> diagnostics,
        ref string detectedFormat)
    {
        using JsonDocument document = LoadJsonDocument(path, diagnostics);
        if (diagnostics.Count > 0)
            return;

        JsonElement root = document.RootElement;
        if (options.Format == ImportInputFormat.RawArguments)
        {
            ParseRawArguments(path, root, options, operations, diagnostics, ref detectedFormat);
            return;
        }

        if (options.Format == ImportInputFormat.ToolEnvelope)
        {
            detectedFormat = "tool-envelope";
            TryAppendEnvelopeOperation(path, root, sourceIndex: 1, sourceLine: null, diagnostics, operations);
            return;
        }

        if (options.Format == ImportInputFormat.ToolCallArray)
        {
            ParseToolCallArray(path, root, diagnostics, operations, ref detectedFormat);
            return;
        }

        if (options.Format != ImportInputFormat.Auto)
        {
            diagnostics.Add(CreateError("IMPORT010", $"Unsupported format '{FormatToLabel(options.Format)}' for JSON file parser.", path));
            return;
        }

        if (root.ValueKind == JsonValueKind.Array || HasProperty(root, "tool_calls"))
        {
            ParseToolCallArray(path, root, diagnostics, operations, ref detectedFormat);
            return;
        }

        if (LooksLikeEnvelope(root))
        {
            detectedFormat = "tool-envelope";
            TryAppendEnvelopeOperation(path, root, sourceIndex: 1, sourceLine: null, diagnostics, operations);
            return;
        }

        diagnostics.Add(CreateError(
            "IMPORT010",
            "Could not auto-detect input shape. Use --format raw-arguments with --tool for argument-only JSON.",
            path,
            jsonPointer: "/"));
    }

    private void ParseRawArguments(
        string path,
        JsonElement root,
        ImportNormalizeOptions options,
        List<ImportPlanOperation> operations,
        List<ImportDiagnostic> diagnostics,
        ref string detectedFormat)
    {
        detectedFormat = "raw-arguments";
        if (string.IsNullOrWhiteSpace(options.ToolName))
        {
            diagnostics.Add(CreateError("IMPORT011", "--tool is required when --format raw-arguments is used.", path, jsonPointer: "/"));
            return;
        }

        AppendToolOperation(
            operations,
            options.ToolName,
            root.Clone(),
            sourceIndex: 1,
            sourceLine: null,
            sourceCallId: null);
    }

    private void ParseToolCallArray(
        string path,
        JsonElement root,
        List<ImportDiagnostic> diagnostics,
        List<ImportPlanOperation> operations,
        ref string detectedFormat)
    {
        detectedFormat = "tool-call-array";
        if (root.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement element in root.EnumerateArray())
            {
                index++;
                TryAppendEnvelopeOperation(path, element, index, null, diagnostics, operations);
            }

            return;
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tool_calls", out JsonElement toolCalls))
        {
            if (toolCalls.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(CreateError("IMPORT014", "tool_calls must be an array.", path, jsonPointer: "/tool_calls"));
                return;
            }

            int index = 0;
            foreach (JsonElement element in toolCalls.EnumerateArray())
            {
                index++;
                TryAppendEnvelopeOperation(path, element, index, null, diagnostics, operations);
            }

            return;
        }

        diagnostics.Add(CreateError("IMPORT010", "Expected an array or an object with a tool_calls array.", path, jsonPointer: "/"));
    }

    private void ParseJsonLinesFile(
        string path,
        ImportNormalizeOptions options,
        List<ImportPlanOperation> operations,
        List<ImportDiagnostic> diagnostics,
        ref string detectedFormat)
    {
        string? firstNonEmpty = FirstNonEmptyLine(path);
        if (firstNonEmpty is null)
        {
            diagnostics.Add(CreateError("IMPORT010", "JSON Lines input is empty.", path));
            return;
        }

        ImportInputFormat lineFormat = options.Format;
        if (lineFormat == ImportInputFormat.Auto)
            lineFormat = DetectJsonLinesFormat(firstNonEmpty);

        if (lineFormat == ImportInputFormat.ToolCallsJsonl)
        {
            detectedFormat = "tool-calls-jsonl";
            ParseToolCallsJsonLines(path, operations, diagnostics);
            return;
        }

        if (lineFormat == ImportInputFormat.StdioJsonl)
        {
            detectedFormat = "stdio-jsonl";
            ParseStdioJsonLines(path, operations, diagnostics);
            return;
        }

        diagnostics.Add(CreateError("IMPORT010", "Could not auto-detect JSON Lines format.", path));
    }

    private void ParseToolCallsJsonLines(
        string path,
        List<ImportPlanOperation> operations,
        List<ImportDiagnostic> diagnostics)
    {
        int lineNumber = 0;
        int fallbackIndex = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            fallbackIndex++;
            using JsonDocument? document = TryParseJsonLine(path, line, lineNumber, diagnostics);
            if (document is null)
                continue;

            JsonElement root = document.RootElement;
            int sourceIndex = ReadSourceIndex(root, fallbackIndex);
            TryAppendEnvelopeOperation(path, root, sourceIndex, lineNumber, diagnostics, operations);
        }
    }

    private void ParseStdioJsonLines(
        string path,
        List<ImportPlanOperation> operations,
        List<ImportDiagnostic> diagnostics)
    {
        int lineNumber = 0;
        int fallbackIndex = 0;
        foreach (string line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            fallbackIndex++;
            using JsonDocument? document = TryParseJsonLine(path, line, lineNumber, diagnostics);
            if (document is null)
                continue;

            JsonElement root = document.RootElement;
            int sourceIndex = ReadSourceIndex(root, fallbackIndex);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("request", out JsonElement request))
            {
                diagnostics.Add(CreateError("IMPORT010", "stdio.jsonl line must contain a request object.", path, lineNumber, sourceIndex, jsonPointer: "/request"));
                continue;
            }

            if (request.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateError("IMPORT014", "request must be an object.", path, lineNumber, sourceIndex, jsonPointer: "/request"));
                continue;
            }

            if (!TryReadString(request, "command", out string? command))
            {
                diagnostics.Add(CreateError("IMPORT011", "request.command is required.", path, lineNumber, sourceIndex, jsonPointer: "/request/command"));
                continue;
            }

            AppendConsoleOperation(operations, command!, request.Clone(), sourceIndex, lineNumber);

            if (root.TryGetProperty("response", out JsonElement response)
                && response.ValueKind == JsonValueKind.Object
                && response.TryGetProperty("ok", out JsonElement okElement)
                && okElement.ValueKind == JsonValueKind.False)
            {
                diagnostics.Add(CreateWarning("IMPORT203", "Transcript response was ok:false; request is still normalized for validation.", path, lineNumber, sourceIndex, command, "/response/ok"));
            }
        }
    }

    private void TryAppendEnvelopeOperation(
        string path,
        JsonElement element,
        int sourceIndex,
        int? sourceLine,
        List<ImportDiagnostic> diagnostics,
        List<ImportPlanOperation> operations)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateError("IMPORT014", "Tool call entry must be an object.", path, sourceLine, sourceIndex, jsonPointer: "/"));
            return;
        }

        if (!TryNormalizeEnvelope(element, out string? name, out JsonElement arguments, out string? callId, out string? namePointer, out string? argumentsPointer, out string? errorCode, out string? errorMessage))
        {
            string pointer = errorCode == "IMPORT011" ? namePointer ?? "/name" : argumentsPointer ?? "/arguments";
            diagnostics.Add(CreateError(errorCode!, errorMessage!, path, sourceLine, sourceIndex, callId, name, pointer));
            return;
        }

        AppendToolOperation(operations, name!, arguments, sourceIndex, sourceLine, callId);
    }

    private bool TryNormalizeEnvelope(
        JsonElement element,
        out string? name,
        out JsonElement arguments,
        out string? callId,
        out string? namePointer,
        out string? argumentsPointer,
        out string? errorCode,
        out string? errorMessage)
    {
        name = null;
        arguments = default;
        callId = ReadOptionalString(element, "id") ?? ReadOptionalString(element, "tool_call_id");
        namePointer = "/name";
        argumentsPointer = "/arguments";
        errorCode = null;
        errorMessage = null;

        if (TryReadString(element, "name", out name))
        {
            if (TryReadArguments(element, "arguments", out arguments, out errorCode, out errorMessage))
                return true;

            if (TryReadArguments(element, "input", out arguments, out errorCode, out errorMessage))
            {
                argumentsPointer = "/input";
                return true;
            }

            errorCode ??= "IMPORT012";
            errorMessage ??= "Tool arguments are required.";
            return false;
        }

        if (TryReadString(element, "tool_name", out name))
        {
            namePointer = "/tool_name";
            if (TryReadArguments(element, "arguments", out arguments, out errorCode, out errorMessage))
                return true;

            errorCode ??= "IMPORT012";
            errorMessage ??= "Tool arguments are required.";
            return false;
        }

        if (element.TryGetProperty("function", out JsonElement functionElement)
            && functionElement.ValueKind == JsonValueKind.Object)
        {
            namePointer = "/function/name";
            argumentsPointer = "/function/arguments";
            if (!TryReadString(functionElement, "name", out name))
            {
                errorCode = "IMPORT011";
                errorMessage = "function.name is required.";
                return false;
            }

            if (TryReadArguments(functionElement, "arguments", out arguments, out errorCode, out errorMessage))
                return true;

            errorCode ??= "IMPORT012";
            errorMessage ??= "function.arguments are required.";
            return false;
        }

        if (TryReadString(element, "method", out string? method)
            && string.Equals(method, "tools/call", StringComparison.Ordinal)
            && element.TryGetProperty("params", out JsonElement paramsElement)
            && paramsElement.ValueKind == JsonValueKind.Object)
        {
            namePointer = "/params/name";
            argumentsPointer = "/params/arguments";
            if (!TryReadString(paramsElement, "name", out name))
            {
                errorCode = "IMPORT011";
                errorMessage = "params.name is required.";
                return false;
            }

            if (TryReadArguments(paramsElement, "arguments", out arguments, out errorCode, out errorMessage))
                return true;

            errorCode ??= "IMPORT012";
            errorMessage ??= "params.arguments are required.";
            return false;
        }

        errorCode = "IMPORT011";
        errorMessage = "Tool name is required.";
        return false;
    }

    private static bool TryReadArguments(
        JsonElement element,
        string propertyName,
        out JsonElement arguments,
        out string? errorCode,
        out string? errorMessage)
    {
        arguments = default;
        errorCode = null;
        errorMessage = null;

        if (!element.TryGetProperty(propertyName, out JsonElement rawArguments))
            return false;

        if (rawArguments.ValueKind == JsonValueKind.String)
        {
            string? json = rawArguments.GetString();
            if (string.IsNullOrWhiteSpace(json))
            {
                errorCode = "IMPORT012";
                errorMessage = $"{propertyName} must not be empty.";
                return false;
            }

            try
            {
                arguments = ImportJson.ParseElement(json).Clone();
                return true;
            }
            catch (JsonException ex)
            {
                errorCode = "IMPORT013";
                errorMessage = $"{propertyName} string is not valid JSON: {ex.Message}";
                return false;
            }
        }

        arguments = rawArguments.Clone();
        return true;
    }

    private void AppendToolOperation(
        List<ImportPlanOperation> operations,
        string toolName,
        JsonElement arguments,
        int sourceIndex,
        int? sourceLine,
        string? sourceCallId)
    {
        operations.Add(new ImportPlanOperation
        {
            OperationId = FormatOperationId(operations.Count + 1),
            SourceIndex = sourceIndex,
            SourceLine = sourceLine,
            SourceCallId = sourceCallId,
            Kind = "tool_call",
            Name = toolName,
            Arguments = arguments.Clone(),
            Effect = _validator.ResolveToolEffect(toolName),
        });
    }

    private void AppendConsoleOperation(
        List<ImportPlanOperation> operations,
        string commandName,
        JsonElement request,
        int sourceIndex,
        int sourceLine)
    {
        operations.Add(new ImportPlanOperation
        {
            OperationId = FormatOperationId(operations.Count + 1),
            SourceIndex = sourceIndex,
            SourceLine = sourceLine,
            SourceCallId = null,
            Kind = "console_command",
            Name = commandName,
            Arguments = request.Clone(),
            Effect = _validator.ResolveCommandEffect(commandName),
        });
    }

    private JsonDocument LoadJsonDocument(string path, List<ImportDiagnostic> diagnostics)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), _documentOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(CreateError("IMPORT001", ex.Message, path, line: (int?)ex.LineNumber, column: (int?)ex.BytePositionInLine));
            return JsonDocument.Parse("{}");
        }
    }

    private JsonDocument? TryParseJsonLine(string path, string line, int lineNumber, List<ImportDiagnostic> diagnostics)
    {
        try
        {
            return JsonDocument.Parse(line, _documentOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(CreateError("IMPORT002", ex.Message, path, lineNumber, column: (int?)ex.BytePositionInLine));
            return null;
        }
    }

    private ImportNormalizeResult BuildResult(
        VoxelForgeImportPlan? plan,
        ImportNormalizeOptions options,
        string path,
        string sourceFormat,
        IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        int errorCount = CountDiagnostics(diagnostics, ImportDiagnosticSeverity.Error);
        int warningCount = CountDiagnostics(diagnostics, ImportDiagnosticSeverity.Warning);
        int readOnlyCount = CountReadOnlyOperations(plan);
        bool success = errorCount == 0;
        if (!options.Strict && plan is not null)
            success = true;

        ImportReport report = new()
        {
            Status = errorCount == 0 ? "succeeded" : "failed",
            SourceFormat = sourceFormat,
            OperationCount = plan?.Operations.Count ?? 0,
            AcceptedOperationCount = CountAcceptedOperations(plan),
            SkippedReadOnlyCount = readOnlyCount,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Diagnostics = diagnostics,
        };

        return new ImportNormalizeResult
        {
            Success = success,
            Plan = plan,
            Diagnostics = diagnostics,
            Report = report,
        };
    }

    private VoxelForgeImportPlan CreatePlan(
        string path,
        string detectedFormat,
        ImportNormalizeOptions options,
        IReadOnlyList<ImportPlanOperation> operations)
    {
        DateTimeOffset capturedAt = options.CapturedAtUtc ?? DateTimeOffset.UtcNow;
        return new VoxelForgeImportPlan
        {
            Source = new ImportPlanSource
            {
                Format = detectedFormat,
                Path = path,
                Sha256 = File.Exists(path) ? ImportJson.ComputeSha256(path) : string.Empty,
                CapturedAtUtc = capturedAt.ToString("O"),
            },
            Options = new ImportPlanOptions
            {
                Strict = options.Strict,
                MaxOperations = options.MaxOperations,
                MaxGeneratedVoxels = options.MaxGeneratedVoxels,
            },
            Operations = operations.ToArray(),
        };
    }

    private static ImportInputFormat DetectJsonLinesFormat(string firstLine)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(firstLine);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("request", out JsonElement _))
                return ImportInputFormat.StdioJsonl;
            if (root.ValueKind == JsonValueKind.Object && (root.TryGetProperty("name", out JsonElement _) || root.TryGetProperty("tool_call_id", out JsonElement _)))
                return ImportInputFormat.ToolCallsJsonl;
        }
        catch (JsonException)
        {
        }

        return ImportInputFormat.Auto;
    }

    private static bool LooksLikeJsonLines(string path)
    {
        return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmptyLine(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }

        return null;
    }

    private static bool LooksLikeEnvelope(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        return HasProperty(element, "name")
            || HasProperty(element, "tool_name")
            || HasProperty(element, "function")
            || HasProperty(element, "params");
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out JsonElement _);
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        if (TryReadString(element, propertyName, out string? value))
            return value;
        return null;
    }

    private static int ReadSourceIndex(JsonElement element, int fallbackIndex)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("index", out JsonElement indexElement)
            && indexElement.ValueKind == JsonValueKind.Number
            && indexElement.TryGetInt32(out int sourceIndex))
            return sourceIndex;

        return fallbackIndex;
    }

    private static int CountDiagnostics(IReadOnlyList<ImportDiagnostic> diagnostics, ImportDiagnosticSeverity severity)
    {
        int count = 0;
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == severity)
                count++;
        }

        return count;
    }

    private static int CountReadOnlyOperations(VoxelForgeImportPlan? plan)
    {
        if (plan is null)
            return 0;

        int count = 0;
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            if (plan.Operations[i].Effect == "read_only")
                count++;
        }

        return count;
    }

    private static int CountAcceptedOperations(VoxelForgeImportPlan? plan)
    {
        if (plan is null)
            return 0;

        int count = 0;
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            if (plan.Operations[i].Effect != "unsupported")
                count++;
        }

        return count;
    }

    private static string FormatOperationId(int index)
    {
        return "op-" + index.ToString("D6");
    }

    private static string FormatToLabel(ImportInputFormat format)
    {
        return format switch
        {
            ImportInputFormat.Auto => "auto",
            ImportInputFormat.ToolEnvelope => "tool-envelope",
            ImportInputFormat.RawArguments => "raw-arguments",
            ImportInputFormat.ToolCallArray => "tool-call-array",
            ImportInputFormat.ToolCallsJsonl => "tool-calls-jsonl",
            ImportInputFormat.StdioJsonl => "stdio-jsonl",
            _ => "auto",
        };
    }

    private static ImportDiagnostic CreateError(
        string code,
        string message,
        string sourcePath,
        int? line = null,
        int? operationIndex = null,
        string? sourceCallId = null,
        string? toolName = null,
        string? jsonPointer = null,
        int? column = null)
    {
        return new ImportDiagnostic
        {
            Severity = ImportDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column,
            OperationIndex = operationIndex,
            ToolName = toolName,
            SourceCallId = sourceCallId,
            JsonPointer = jsonPointer,
        };
    }

    private static ImportDiagnostic CreateWarning(
        string code,
        string message,
        string sourcePath,
        int? line,
        int operationIndex,
        string? toolName,
        string? jsonPointer)
    {
        return new ImportDiagnostic
        {
            Severity = ImportDiagnosticSeverity.Warning,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            OperationIndex = operationIndex,
            ToolName = toolName,
            JsonPointer = jsonPointer,
        };
    }

}
