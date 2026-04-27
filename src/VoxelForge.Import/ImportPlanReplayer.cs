using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Import;

public sealed class ImportReplayOptions
{
    public required string OutputPath { get; init; }
    public string? ProjectDirectory { get; init; }
    public string? InitialModelPath { get; init; }
}

public sealed class ImportReplayResult
{
    public required bool Success { get; init; }
    public required ImportReport Report { get; init; }
    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }
    public string? OutputPath { get; init; }
}

public sealed class ImportPlanReplayer
{
    private readonly ImportPlanValidator _validator;
    private readonly ILoggerFactory _loggerFactory;

    public ImportPlanReplayer()
        : this(new ImportPlanValidator(), NullLoggerFactory.Instance)
    {
    }

    public ImportPlanReplayer(ImportPlanValidator validator, ILoggerFactory loggerFactory)
    {
        _validator = validator;
        _loggerFactory = loggerFactory;
    }

    public ImportReplayResult ReplayPlanFile(string planPath, ImportReplayOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        ArgumentNullException.ThrowIfNull(options);

        if (!File.Exists(planPath))
        {
            ImportDiagnostic diagnostic = CreateDiagnostic(
                ImportDiagnosticSeverity.Error,
                "IMPORT300",
                $"Import plan file not found: {planPath}",
                planPath);
            return BuildResult(null, planPath, "import-plan", [diagnostic], null);
        }

        VoxelForgeImportPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<VoxelForgeImportPlan>(File.ReadAllText(planPath), ImportJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            ImportDiagnostic diagnostic = CreateDiagnostic(
                ImportDiagnosticSeverity.Error,
                "IMPORT301",
                "Import plan JSON is invalid: " + ex.Message,
                planPath,
                line: (int?)ex.LineNumber,
                column: ex.BytePositionInLine);
            return BuildResult(null, planPath, "import-plan", [diagnostic], null);
        }

        if (plan is null)
        {
            ImportDiagnostic diagnostic = CreateDiagnostic(
                ImportDiagnosticSeverity.Error,
                "IMPORT301",
                "Import plan JSON did not contain a plan object.",
                planPath);
            return BuildResult(null, planPath, "import-plan", [diagnostic], null);
        }

        return ReplayToFile(plan, options, planPath, cancellationToken);
    }

    public ImportReplayResult ReplayToFile(VoxelForgeImportPlan plan, ImportReplayOptions options, CancellationToken cancellationToken = default)
    {
        return ReplayToFile(plan, options, plan.Source.Path, cancellationToken);
    }

    private ImportReplayResult ReplayToFile(VoxelForgeImportPlan plan, ImportReplayOptions options, string diagnosticSourcePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<ImportDiagnostic>();
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            diagnostics.Add(CreateDiagnostic(
                ImportDiagnosticSeverity.Error,
                "IMPORT302",
                "Replay requires an explicit --out path.",
                diagnosticSourcePath,
                jsonPointer: "/out"));
            return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);
        }

        diagnostics.AddRange(_validator.ValidateOperations(
            plan.Operations,
            new ImportNormalizeOptions
            {
                Strict = plan.Options.Strict,
                MaxOperations = plan.Options.MaxOperations,
                MaxGeneratedVoxels = plan.Options.MaxGeneratedVoxels,
            },
            [],
            diagnosticSourcePath));

        if (CountErrors(diagnostics) > 0)
            return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);

        string outputPath = Path.GetFullPath(options.OutputPath);
        string outputDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);
        string tempDirectory = Path.Combine(outputDirectory, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string tempPath = Path.Combine(tempDirectory, Path.GetFileName(outputPath));

        ImportReplaySession session = CreateSession(options.ProjectDirectory ?? outputDirectory);
        try
        {
            if (!string.IsNullOrWhiteSpace(options.InitialModelPath))
            {
                ApplicationServiceResult loadResult = session.ProjectLifecycleService.Load(
                    session.McpSession.Document,
                    session.McpSession.UndoStack,
                    session.McpSession.Events,
                    new LoadProjectRequest(options.InitialModelPath));
                if (!loadResult.Success)
                {
                    diagnostics.Add(CreateDiagnostic(
                        ImportDiagnosticSeverity.Error,
                        "IMPORT303",
                        "Initial model load failed: " + loadResult.Message,
                        diagnosticSourcePath));
                    return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);
                }
            }

            diagnostics.AddRange(ValidateReplayState(session, plan, diagnosticSourcePath));
            if (CountErrors(diagnostics) > 0)
                return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);

            for (int i = 0; i < plan.Operations.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ImportPlanOperation operation = plan.Operations[i];
                if (operation.Effect == "read_only")
                    continue;

                ImportDiagnostic? replayDiagnostic = ReplayOperation(session, operation, diagnosticSourcePath, cancellationToken);
                if (replayDiagnostic is not null)
                {
                    diagnostics.Add(replayDiagnostic);
                    return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);
                }
            }

            ApplicationServiceResult saveResult = session.ProjectLifecycleService.Save(
                session.McpSession.Document,
                session.McpSession.Events,
                new SaveProjectRequest(tempPath));
            if (!saveResult.Success)
            {
                diagnostics.Add(CreateDiagnostic(
                    ImportDiagnosticSeverity.Error,
                    "IMPORT304",
                    "Saving materialized model failed: " + saveResult.Message,
                    diagnosticSourcePath));
                return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, null);
            }

            File.Move(tempPath, outputPath, overwrite: true);
            return BuildResult(plan, diagnosticSourcePath, plan.Source.Format, diagnostics, outputPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static IReadOnlyList<ImportDiagnostic> ValidateReplayState(
        ImportReplaySession session,
        VoxelForgeImportPlan plan,
        string sourcePath)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var knownRegions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in session.McpSession.Document.Labels.Regions)
            knownRegions.Add(entry.Key.Value);

        for (int i = 0; i < plan.Operations.Count; i++)
        {
            ImportPlanOperation operation = plan.Operations[i];
            if (operation.Effect == "read_only")
                continue;

            if (operation.Kind == "tool_call" && operation.Name == "new_model")
            {
                knownRegions.Clear();
                continue;
            }

            if (operation.Kind == "tool_call" && operation.Name == "create_region")
            {
                if (!TryReadString(operation.Arguments, "name", out string? regionName))
                    continue;

                if (knownRegions.Contains(regionName))
                {
                    diagnostics.Add(CreateReplayStateError(
                        "Region '" + regionName + "' already exists before this create_region operation.",
                        sourcePath,
                        operation,
                        "/arguments/name"));
                    continue;
                }

                if (TryReadString(operation.Arguments, "parent_id", out string? parentId) && !knownRegions.Contains(parentId))
                {
                    diagnostics.Add(CreateReplayStateError(
                        "Parent region '" + parentId + "' does not exist before this create_region operation.",
                        sourcePath,
                        operation,
                        "/arguments/parent_id"));
                    continue;
                }

                knownRegions.Add(regionName);
                continue;
            }

            if (operation.Kind == "tool_call" && operation.Name == "delete_region")
            {
                if (TryReadString(operation.Arguments, "region_id", out string? regionId))
                {
                    if (!knownRegions.Contains(regionId))
                    {
                        diagnostics.Add(CreateReplayStateError(
                            "Region '" + regionId + "' does not exist before this delete_region operation.",
                            sourcePath,
                            operation,
                            "/arguments/region_id"));
                        continue;
                    }

                    knownRegions.Remove(regionId);
                }

                continue;
            }

            if (operation.Kind == "tool_call" && operation.Name == "assign_voxels_to_region")
            {
                if (TryReadString(operation.Arguments, "region_id", out string? regionId) && !knownRegions.Contains(regionId))
                {
                    diagnostics.Add(CreateReplayStateError(
                        "Region '" + regionId + "' does not exist before this assign_voxels_to_region operation.",
                        sourcePath,
                        operation,
                        "/arguments/region_id"));
                }
            }
        }

        return diagnostics;
    }

    private ImportDiagnostic? ReplayOperation(
        ImportReplaySession session,
        ImportPlanOperation operation,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (operation.Kind == "tool_call")
            return ReplayToolCall(session, operation, sourcePath, cancellationToken);

        if (operation.Kind == "console_command")
            return ReplayConsoleCommand(session, operation, sourcePath);

        return CreateReplayError("Unsupported operation kind '" + operation.Kind + "'.", sourcePath, operation);
    }

    private ImportDiagnostic? ReplayToolCall(
        ImportReplaySession session,
        ImportPlanOperation operation,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (session.LlmHandlers.TryGetValue(operation.Name, out IToolHandler? handler))
        {
            ToolHandlerResult handlerResult;
            lock (session.McpSession.SyncRoot)
            {
                cancellationToken.ThrowIfCancellationRequested();
                handlerResult = handler.Handle(
                    operation.Arguments,
                    session.McpSession.Document.Model,
                    session.McpSession.Document.Labels,
                    session.McpSession.Document.Clips);

                if (!handlerResult.IsError && handlerResult.MutationIntent is not null)
                {
                    ApplicationServiceResult applicationResult = session.LlmApplicationService.ApplyMutationIntents(
                        session.McpSession.Document,
                        session.McpSession.UndoStack,
                        session.McpSession.Events,
                        new ApplyLlmMutationIntentsRequest([handlerResult.MutationIntent]));
                    if (!applicationResult.Success)
                        return CreateReplayError(applicationResult.Message, sourcePath, operation);
                }
            }

            return handlerResult.IsError ? CreateReplayError(handlerResult.Content, sourcePath, operation) : null;
        }

        if (session.McpTools.TryGetValue(operation.Name, out IVoxelForgeMcpTool? tool))
        {
            McpToolInvocationResult result = tool.Invoke(operation.Arguments, cancellationToken);
            return result.Success ? null : CreateReplayError(result.Message, sourcePath, operation);
        }

        return CreateReplayError("Unsupported tool '" + operation.Name + "'.", sourcePath, operation);
    }

    private ImportDiagnostic? ReplayConsoleCommand(ImportReplaySession session, ImportPlanOperation operation, string sourcePath)
    {
        StdioCommandRequest? request;
        try
        {
            request = operation.Arguments.Deserialize<StdioCommandRequest>(ImportJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            return CreateReplayError("Console command request is invalid: " + ex.Message, sourcePath, operation, "/arguments");
        }

        string[] args = request?.Args ?? [];
        CommandResult result = session.CommandRouter.Execute(operation.Name, args, session.McpSession.CommandContext);
        return result.Success ? null : CreateReplayError(result.Message, sourcePath, operation);
    }

    private ImportReplaySession CreateSession(string projectDirectory)
    {
        var config = new EditorConfigState();
        var mcpSession = new VoxelForgeMcpSession(config, _loggerFactory);
        var voxelEditingService = new VoxelEditingService();
        var voxelQueryService = new VoxelQueryService();
        var regionEditingService = new RegionEditingService();
        var paletteMaterialService = new PaletteMaterialService();
        var projectLifecycleService = new ProjectLifecycleService(_loggerFactory);
        var intentService = new VoxelMutationIntentService();
        var primitiveGenerationService = new VoxelPrimitiveGenerationService();
        var llmApplicationService = new LlmToolApplicationService(voxelEditingService);
        var pathResolver = new ModelPathResolver(new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory });

        var llmHandlers = new Dictionary<string, IToolHandler>(StringComparer.Ordinal)
        {
            ["set_voxels"] = new SetVoxelsHandler(intentService),
            ["remove_voxels"] = new RemoveVoxelsHandler(intentService),
            ["apply_voxel_primitives"] = new ApplyVoxelPrimitivesHandler(primitiveGenerationService),
        };

        var mcpTools = new Dictionary<string, IVoxelForgeMcpTool>(StringComparer.Ordinal)
        {
            ["new_model"] = new NewModelMcpTool(mcpSession, _loggerFactory, config),
            ["set_palette_entry"] = new SetPaletteEntryMcpTool(mcpSession, paletteMaterialService),
            ["set_grid_hint"] = new SetGridHintMcpTool(mcpSession, voxelEditingService),
            ["create_region"] = new CreateRegionMcpTool(mcpSession, regionEditingService),
            ["assign_voxels_to_region"] = new AssignVoxelsToRegionMcpTool(mcpSession, regionEditingService),
            ["delete_region"] = new DeleteRegionMcpTool(mcpSession, regionEditingService),
            ["fill_box"] = new FillBoxMcpTool(new FillCommand(voxelEditingService), mcpSession),
            ["clear_model"] = new ClearModelMcpTool(new ClearCommand(voxelEditingService), mcpSession),
            ["save_model"] = new SaveModelMcpTool(mcpSession, projectLifecycleService, pathResolver),
            ["load_model"] = new LoadModelMcpTool(mcpSession, projectLifecycleService, pathResolver),
        };

        IConsoleCommand[] commands =
        [
            new SetVoxelConsoleCommand(voxelEditingService),
            new RemoveVoxelConsoleCommand(voxelEditingService),
            new FillCommand(voxelEditingService),
            new ClearCommand(voxelEditingService),
            new GridCommand(voxelEditingService),
            new PaletteCommand(paletteMaterialService),
            new ListRegionsCommand(regionEditingService),
            new LabelVoxelCommand(regionEditingService),
            new UndoCommand(),
            new RedoCommand(),
        ];
        var commandRouter = new CommandRouter(commands, _loggerFactory.CreateLogger<CommandRouter>());

        return new ImportReplaySession(
            mcpSession,
            projectLifecycleService,
            llmApplicationService,
            llmHandlers,
            mcpTools,
            commandRouter);
    }

    private static ImportReplayResult BuildResult(
        VoxelForgeImportPlan? plan,
        string sourcePath,
        string sourceFormat,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        string? outputPath)
    {
        int errorCount = CountErrors(diagnostics);
        int warningCount = CountWarnings(diagnostics);
        ImportReport report = new()
        {
            Status = errorCount == 0 ? "succeeded" : "failed",
            SourceFormat = sourceFormat,
            OperationCount = plan?.Operations.Count ?? 0,
            AcceptedOperationCount = CountAcceptedOperations(plan),
            SkippedReadOnlyCount = CountReadOnlyOperations(plan),
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Operations = BuildReportOperations(plan),
            Diagnostics = diagnostics,
        };

        return new ImportReplayResult
        {
            Success = errorCount == 0,
            Report = report,
            Diagnostics = diagnostics,
            OutputPath = errorCount == 0 ? outputPath : null,
        };
    }

    private static int CountErrors(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        int count = 0;
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == ImportDiagnosticSeverity.Error)
                count++;
        }

        return count;
    }

    private static int CountWarnings(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        int count = 0;
        for (int i = 0; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Severity == ImportDiagnosticSeverity.Warning)
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

    private static IReadOnlyList<ImportReportOperation> BuildReportOperations(VoxelForgeImportPlan? plan)
    {
        if (plan is null)
            return [];

        var operations = new ImportReportOperation[plan.Operations.Count];
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            ImportPlanOperation operation = plan.Operations[i];
            operations[i] = new ImportReportOperation
            {
                OperationId = operation.OperationId,
                SourceIndex = operation.SourceIndex,
                SourceLine = operation.SourceLine,
                SourceCallId = operation.SourceCallId,
                Kind = operation.Kind,
                Name = operation.Name,
                Effect = operation.Effect,
            };
        }

        return operations;
    }

    private static ImportDiagnostic CreateReplayStateError(string message, string sourcePath, ImportPlanOperation operation, string jsonPointer)
    {
        return CreateDiagnostic(
            ImportDiagnosticSeverity.Error,
            "IMPORT311",
            message,
            sourcePath,
            operation.SourceLine,
            operation.SourceIndex,
            operation.SourceCallId,
            operation.Name,
            jsonPointer);
    }

    private static ImportDiagnostic CreateReplayError(string message, string sourcePath, ImportPlanOperation operation, string? jsonPointer = null)
    {
        return CreateDiagnostic(
            ImportDiagnosticSeverity.Error,
            "IMPORT310",
            "Replay failed for '" + operation.Name + "': " + message,
            sourcePath,
            operation.SourceLine,
            operation.SourceIndex,
            operation.SourceCallId,
            operation.Name,
            jsonPointer ?? "/operations/" + Math.Max(0, operation.SourceIndex - 1));
    }

    private static bool TryReadString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static ImportDiagnostic CreateDiagnostic(
        ImportDiagnosticSeverity severity,
        string code,
        string message,
        string sourcePath,
        int? line = null,
        int? operationIndex = null,
        string? sourceCallId = null,
        string? toolName = null,
        string? jsonPointer = null,
        long? column = null)
    {
        return new ImportDiagnostic
        {
            Severity = severity,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = line,
            Column = column,
            OperationIndex = operationIndex,
            SourceCallId = sourceCallId,
            ToolName = toolName,
            JsonPointer = jsonPointer,
        };
    }

    private sealed class ImportReplaySession
    {
        public ImportReplaySession(
            VoxelForgeMcpSession mcpSession,
            ProjectLifecycleService projectLifecycleService,
            LlmToolApplicationService llmApplicationService,
            IReadOnlyDictionary<string, IToolHandler> llmHandlers,
            IReadOnlyDictionary<string, IVoxelForgeMcpTool> mcpTools,
            CommandRouter commandRouter)
        {
            McpSession = mcpSession;
            ProjectLifecycleService = projectLifecycleService;
            LlmApplicationService = llmApplicationService;
            LlmHandlers = llmHandlers;
            McpTools = mcpTools;
            CommandRouter = commandRouter;
        }

        public VoxelForgeMcpSession McpSession { get; }
        public ProjectLifecycleService ProjectLifecycleService { get; }
        public LlmToolApplicationService LlmApplicationService { get; }
        public IReadOnlyDictionary<string, IToolHandler> LlmHandlers { get; }
        public IReadOnlyDictionary<string, IVoxelForgeMcpTool> McpTools { get; }
        public CommandRouter CommandRouter { get; }
    }
}
