using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Serialization;
using VoxelForge.Core.Services;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkRunExecutionRequest
{
    public required BenchmarkPlannedRun PlannedRun { get; init; }
    public required string InputRoot { get; init; }
    public int? MaxRounds { get; init; }
}

public sealed class BenchmarkRunExecutionResult
{
    public required VoxelModel Model { get; init; }
    public required LabelIndex Labels { get; init; }
    public IReadOnlyList<AnimationClip> Clips { get; init; } = [];
    public ProjectMetadata Metadata { get; init; } = new();
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset EndedAtUtc { get; init; }
    public required string Status { get; init; }
    public int ToolCallCount { get; init; }
    public int FailedToolCallCount { get; init; }
    public int UndoableMutationCount { get; init; }
    public int LlmRounds { get; init; }
    public int ErrorCount { get; init; }
    public string? ToolSchemaSha256 { get; init; }
    public string? ConversationTranscriptJsonl { get; init; }
    public string? ToolCallsTranscriptJsonl { get; init; }
    public string? StdoutLog { get; init; }
    public string? StderrLog { get; init; }
    public BenchmarkFailureArtifact? Failure { get; init; }
}

public interface IBenchmarkExecutionBackend
{
    Task<BenchmarkRunExecutionResult> RunAsync(BenchmarkRunExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class BenchmarkRunSuiteResult
{
    public required string SuiteDirectory { get; init; }
    public required int RunCount { get; init; }
    public required int FailureCount { get; init; }
}

public sealed class BenchmarkRunner
{
    private readonly IBenchmarkExecutionBackend _mcpBackend;
    private readonly BenchmarkArtifactWriter _artifactWriter;
    private readonly BenchmarkComparisonService _comparisonService;

    public BenchmarkRunner()
        : this(
            new McpToolLoopBenchmarkBackend(new BenchmarkCompletionServiceFactory()),
            new BenchmarkArtifactWriter(),
            new BenchmarkComparisonService())
    {
    }

    public BenchmarkRunner(
        IBenchmarkExecutionBackend mcpBackend,
        BenchmarkArtifactWriter artifactWriter,
        BenchmarkComparisonService comparisonService)
    {
        _mcpBackend = mcpBackend;
        _artifactWriter = artifactWriter;
        _comparisonService = comparisonService;
    }

    public async Task<BenchmarkRunSuiteResult> RunAsync(
        BenchmarkRunPlan plan,
        string inputRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRoot);

        if (!string.Equals(plan.Backend, BenchmarkPlanner.DefaultBackend, StringComparison.Ordinal))
            throw new InvalidOperationException($"Execution backend '{plan.Backend}' is not implemented in this task.");

        BenchmarkRunPlan executionPlan = ResolveArtifactRoot(plan, inputRoot);
        string? gitCommit = TryReadGitOutput("rev-parse", "HEAD");
        bool workingTreeDirty = !string.IsNullOrWhiteSpace(TryReadGitOutput("status", "--short"));
        BenchmarkSuiteArtifactContext suite = _artifactWriter.CreateSuite(executionPlan, DateTimeOffset.UtcNow);
        int failures = 0;
        int writtenRuns = 0;
        for (int i = 0; i < executionPlan.Runs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BenchmarkPlannedRun run = executionPlan.Runs[i];
            BenchmarkRunExecutionResult execution = await _mcpBackend.RunAsync(new BenchmarkRunExecutionRequest
            {
                PlannedRun = run,
                InputRoot = inputRoot,
                MaxRounds = executionPlan.MaxRounds,
            }, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(execution.Status, "succeeded", StringComparison.Ordinal))
                failures++;

            _artifactWriter.WriteRunArtifacts(suite, new BenchmarkRunArtifactRequest
            {
                PlannedRun = run,
                Model = execution.Model,
                Labels = execution.Labels,
                Clips = execution.Clips,
                Metadata = execution.Metadata,
                StartedAtUtc = execution.StartedAtUtc,
                EndedAtUtc = execution.EndedAtUtc,
                Status = execution.Status,
                ToolCallCount = execution.ToolCallCount,
                FailedToolCallCount = execution.FailedToolCallCount,
                UndoableMutationCount = execution.UndoableMutationCount,
                LlmRounds = execution.LlmRounds,
                ErrorCount = execution.ErrorCount,
                MaxRounds = executionPlan.MaxRounds,
                GitCommit = gitCommit,
                WorkingTreeDirty = workingTreeDirty,
                Temperature = run.Temperature,
                Seed = run.Seed,
                ToolSchemaSha256 = execution.ToolSchemaSha256,
                ConversationTranscriptJsonl = execution.ConversationTranscriptJsonl,
                ToolCallsTranscriptJsonl = execution.ToolCallsTranscriptJsonl,
                StdoutLog = execution.StdoutLog,
                StderrLog = execution.StderrLog,
                Failure = execution.Failure,
            }, inputRoot);
            writtenRuns++;

            if (failures > 0 && executionPlan.FailFast)
                break;
        }

        _comparisonService.CompareAndWrite(suite.RootPath);
        return new BenchmarkRunSuiteResult
        {
            SuiteDirectory = suite.RootPath,
            RunCount = writtenRuns,
            FailureCount = failures,
        };
    }

    private static BenchmarkRunPlan ResolveArtifactRoot(BenchmarkRunPlan plan, string inputRoot)
    {
        if (Path.IsPathRooted(plan.ArtifactRoot))
            return plan;

        return new BenchmarkRunPlan
        {
            SuiteId = plan.SuiteId,
            ArtifactRoot = Path.GetFullPath(Path.Combine(inputRoot, plan.ArtifactRoot)),
            Backend = plan.Backend,
            FailFast = plan.FailFast,
            MaxRounds = plan.MaxRounds,
            Runs = plan.Runs,
        };
    }

    private static string? TryReadGitOutput(string command, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { command, argument },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return null;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed class McpToolLoopBenchmarkBackend : IBenchmarkExecutionBackend
{
    private const int DefaultMaxRounds = 10;
    private readonly BenchmarkCompletionServiceFactory _completionFactory;
    private readonly BenchmarkMetricsService _metricsService = new();

    public McpToolLoopBenchmarkBackend(BenchmarkCompletionServiceFactory completionFactory)
    {
        _completionFactory = completionFactory;
    }

    public async Task<BenchmarkRunExecutionResult> RunAsync(
        BenchmarkRunExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PlannedRun);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var recorder = new BenchmarkTranscriptRecorder();
        string? workDirectory = null;
        BenchmarkMcpToolCatalog? catalog = null;

        try
        {
            workDirectory = CreateWorkDirectory(request.PlannedRun);
            Directory.CreateDirectory(workDirectory);
            catalog = BenchmarkMcpToolCatalog.Create(workDirectory);
            string systemPrompt = ReadOptionalInput(request.InputRoot, request.PlannedRun.SystemPromptFile)
                ?? "You are a VoxelForge benchmark agent. Use the provided tools to edit the current voxel model, then give a concise final answer.";
            string userPrompt = ReadRequiredInput(request.InputRoot, request.PlannedRun.PromptFile);
            recorder.RecordSystem(systemPrompt);
            recorder.RecordUser(userPrompt);
            InitializeSession(catalog, request, recorder, cancellationToken);

            BenchmarkCompletionServiceResult completionResult = _completionFactory.Create(request.PlannedRun);
            if (!completionResult.Success || completionResult.CompletionService is null)
            {
                throw new InvalidOperationException(completionResult.ErrorMessage ?? "No completion service was configured for benchmark run.");
            }

            BenchmarkToolLoopResult loopResult = await RunToolLoopAsync(
                completionResult.CompletionService,
                catalog.Tools,
                systemPrompt,
                userPrompt,
                request.MaxRounds ?? DefaultMaxRounds,
                recorder,
                cancellationToken).ConfigureAwait(false);

            InvokeTool(catalog.RequiredTools.SaveModel, CreateSaveModelArguments(request.PlannedRun), 0, recorder, cancellationToken);

            DateTimeOffset endedAt = DateTimeOffset.UtcNow;
            return new BenchmarkRunExecutionResult
            {
                Model = catalog.Session.Document.Model,
                Labels = catalog.Session.Document.Labels,
                Clips = catalog.Session.Document.Clips,
                Metadata = new ProjectMetadata { Name = catalog.Session.CurrentModelName },
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                Status = recorder.FailedToolCallCount == 0 ? "succeeded" : "failed",
                ToolCallCount = recorder.ToolCallCount,
                FailedToolCallCount = recorder.FailedToolCallCount,
                UndoableMutationCount = catalog.Session.UndoStack.CanUndo ? 1 : 0,
                LlmRounds = loopResult.LlmRounds,
                ErrorCount = recorder.FailedToolCallCount,
                ToolSchemaSha256 = ComputeToolSchemaSha(catalog.Tools),
                ConversationTranscriptJsonl = recorder.ConversationJsonl,
                ToolCallsTranscriptJsonl = recorder.ToolCallsJsonl,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DateTimeOffset endedAt = DateTimeOffset.UtcNow;
            VoxelModel fallbackModel = catalog?.Session.Document.Model ?? new VoxelModel(NullLogger<VoxelModel>.Instance);
            LabelIndex fallbackLabels = catalog?.Session.Document.Labels ?? new LabelIndex(NullLogger<LabelIndex>.Instance);
            recorder.RecordLogLine("stderr", ex.Message);
            return new BenchmarkRunExecutionResult
            {
                Model = fallbackModel,
                Labels = fallbackLabels,
                Clips = catalog?.Session.Document.Clips ?? [],
                Metadata = new ProjectMetadata { Name = catalog?.Session.CurrentModelName ?? "failed-run" },
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                Status = "failed",
                ToolCallCount = recorder.ToolCallCount,
                FailedToolCallCount = recorder.FailedToolCallCount,
                UndoableMutationCount = catalog?.Session.UndoStack.CanUndo == true ? 1 : 0,
                LlmRounds = recorder.LlmRounds,
                ErrorCount = Math.Max(1, recorder.FailedToolCallCount),
                ToolSchemaSha256 = catalog is null ? null : ComputeToolSchemaSha(catalog.Tools),
                ConversationTranscriptJsonl = recorder.ConversationJsonl,
                ToolCallsTranscriptJsonl = recorder.ToolCallsJsonl,
                StderrLog = recorder.StderrLog,
                Failure = new BenchmarkFailureArtifact
                {
                    Phase = "mcp-tool-loop",
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                },
            };
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static async Task<BenchmarkToolLoopResult> RunToolLoopAsync(
        ICompletionService completionService,
        IReadOnlyList<IVoxelForgeMcpTool> tools,
        string systemPrompt,
        string userPrompt,
        int maxRounds,
        BenchmarkTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        var messages = new List<CompletionMessage>
        {
            new() { Role = "user", TextContent = userPrompt },
        };
        List<ToolDefinition> toolDefinitions = CreateToolDefinitions(tools);
        var toolMap = new Dictionary<string, IVoxelForgeMcpTool>(StringComparer.Ordinal);
        for (int i = 0; i < tools.Count; i++)
            toolMap[tools[i].Name] = tools[i];

        int llmRounds = 0;
        int failedToolCalls = 0;
        for (int round = 1; round <= maxRounds; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            llmRounds++;
            recorder.LlmRounds = llmRounds;
            var response = await completionService.CompleteAsync(new CompletionRequest
            {
                SystemPrompt = systemPrompt,
                Messages = messages,
                Tools = toolDefinitions,
            }, cancellationToken).ConfigureAwait(false);

            if (response.ToolCalls.Count == 0)
            {
                recorder.RecordAssistant(response.TextContent, []);
                break;
            }

            recorder.RecordAssistant(response.TextContent, response.ToolCalls);
            messages.Add(new CompletionMessage
            {
                Role = "assistant",
                TextContent = response.TextContent,
                ToolCalls = response.ToolCalls,
            });

            var toolResults = new List<ToolResultContent>();
            for (int i = 0; i < response.ToolCalls.Count; i++)
            {
                ToolCall call = response.ToolCalls[i];
                ToolResultContent result;
                if (!toolMap.TryGetValue(call.Name, out var tool))
                {
                    result = new ToolResultContent
                    {
                        ToolCallId = call.Id,
                        Content = $"Unknown tool '{call.Name}'.",
                        IsError = true,
                    };
                    recorder.RecordToolCall(round, call, false, result.Content, 0);
                    recorder.RecordTool(call.Id, call.Name, false, result.Content);
                    failedToolCalls++;
                }
                else
                {
                    result = InvokeTool(tool, call, round, recorder, cancellationToken);
                    if (result.IsError)
                        failedToolCalls++;
                }

                toolResults.Add(result);
            }

            messages.Add(new CompletionMessage
            {
                Role = "tool",
                ToolResults = toolResults,
            });
        }

        return new BenchmarkToolLoopResult(llmRounds, failedToolCalls);
    }

    private static ToolResultContent InvokeTool(
        IVoxelForgeMcpTool tool,
        ToolCall call,
        int round,
        BenchmarkTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        McpToolInvocationResult invocation = tool.Invoke(call.Arguments, cancellationToken);
        stopwatch.Stop();
        recorder.RecordToolCall(round, call, invocation.Success, invocation.Message, stopwatch.ElapsedMilliseconds);
        recorder.RecordTool(call.Id, call.Name, invocation.Success, invocation.Message);
        return new ToolResultContent
        {
            ToolCallId = call.Id,
            Content = invocation.Message,
            IsError = !invocation.Success,
        };
    }

    private static void InvokeTool(
        IVoxelForgeMcpTool tool,
        JsonElement arguments,
        int round,
        BenchmarkTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        string toolCallId = "setup-" + recorder.NextSetupToolCallNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var call = new ToolCall
        {
            Id = toolCallId,
            Name = tool.Name,
            Arguments = arguments,
        };
        InvokeTool(tool, call, round, recorder, cancellationToken);
    }

    private static void InitializeSession(
        BenchmarkMcpToolCatalog catalog,
        BenchmarkRunExecutionRequest request,
        BenchmarkTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlannedRun.InitialModel))
        {
            InvokeTool(catalog.RequiredTools.NewModel, CreateNewModelArguments(request.PlannedRun), 0, recorder, cancellationToken);
        }
        else
        {
            string source = ResolveInputPath(request.InputRoot, request.PlannedRun.InitialModel);
            string target = Path.Combine(catalog.ProjectDirectory, "initial.vforge");
            File.Copy(source, target, overwrite: false);
            InvokeTool(catalog.RequiredTools.LoadModel, JsonSerializer.SerializeToElement(new { name = "initial" }), 0, recorder, cancellationToken);
        }

        ApplyPaletteFile(catalog, request, recorder, cancellationToken);
    }

    private static void ApplyPaletteFile(
        BenchmarkMcpToolCatalog catalog,
        BenchmarkRunExecutionRequest request,
        BenchmarkTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlannedRun.PaletteFile))
            return;

        string palettePath = ResolveInputPath(request.InputRoot, request.PlannedRun.PaletteFile);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(palettePath));
        JsonElement root = document.RootElement;
        JsonElement entriesElement = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("entries", out JsonElement entries))
                entriesElement = entries;
            else if (root.TryGetProperty("palette", out JsonElement palette))
                entriesElement = palette;
        }

        if (entriesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Palette file must contain an array, entries array, or palette array.");

        foreach (JsonElement entry in entriesElement.EnumerateArray())
        {
            JsonElement arguments = NormalizePaletteEntry(entry);
            InvokeTool(catalog.RequiredTools.SetPaletteEntry, arguments, 0, recorder, cancellationToken);
        }
    }

    private static JsonElement NormalizePaletteEntry(JsonElement entry)
    {
        int index = entry.GetProperty("index").GetInt32();
        string name = entry.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? "material"
            : "material";
        int red = ReadColor(entry, "r", "red");
        int green = ReadColor(entry, "g", "green");
        int blue = ReadColor(entry, "b", "blue");
        int alpha = entry.TryGetProperty("a", out JsonElement alphaElement) && alphaElement.ValueKind == JsonValueKind.Number
            ? alphaElement.GetInt32()
            : ReadColor(entry, "a", "alpha", 255);
        return JsonSerializer.SerializeToElement(new
        {
            index,
            name,
            r = red,
            g = green,
            b = blue,
            a = alpha,
        });
    }

    private static int ReadColor(JsonElement entry, string shortName, string longName, int defaultValue = 0)
    {
        if (entry.TryGetProperty(shortName, out JsonElement shortElement) && shortElement.ValueKind == JsonValueKind.Number)
            return shortElement.GetInt32();
        if (entry.TryGetProperty(longName, out JsonElement longElement) && longElement.ValueKind == JsonValueKind.Number)
            return longElement.GetInt32();
        return defaultValue;
    }

    private static JsonElement CreateNewModelArguments(BenchmarkPlannedRun run)
    {
        return JsonSerializer.SerializeToElement(new
        {
            name = CreateSafeModelName(run),
        });
    }

    private static JsonElement CreateSaveModelArguments(BenchmarkPlannedRun run)
    {
        return JsonSerializer.SerializeToElement(new
        {
            name = CreateSafeModelName(run) + "-final",
        });
    }

    private static string CreateSafeModelName(BenchmarkPlannedRun run)
    {
        return $"{run.CaseId}-{run.VariantId}-trial-{run.Trial}";
    }

    private static List<ToolDefinition> CreateToolDefinitions(IReadOnlyList<IVoxelForgeMcpTool> tools)
    {
        var result = new List<ToolDefinition>(tools.Count);
        for (int i = 0; i < tools.Count; i++)
        {
            result.Add(new ToolDefinition
            {
                Name = tools[i].Name,
                Description = tools[i].Description,
                ParametersSchema = tools[i].InputSchema,
            });
        }

        return result;
    }

    private string ComputeToolSchemaSha(IReadOnlyList<IVoxelForgeMcpTool> tools)
    {
        var definitions = new List<object>(tools.Count);
        for (int i = 0; i < tools.Count; i++)
        {
            definitions.Add(new
            {
                name = tools[i].Name,
                description = tools[i].Description,
                input_schema = tools[i].InputSchema.Clone(),
                read_only = tools[i].IsReadOnly,
            });
        }

        string json = JsonSerializer.Serialize(definitions, BenchmarkJson.WriteIndentedOptions);
        return _metricsService.ComputeTextSha256(json);
    }

    private static string? ReadOptionalInput(string inputRoot, string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : File.ReadAllText(ResolveInputPath(inputRoot, path));
    }

    private static string ReadRequiredInput(string inputRoot, string path)
    {
        return File.ReadAllText(ResolveInputPath(inputRoot, path));
    }

    private static string ResolveInputPath(string inputRoot, string path)
    {
        return Path.Combine(inputRoot, path);
    }

    private static string CreateWorkDirectory(BenchmarkPlannedRun run)
    {
        string suffix = Guid.NewGuid().ToString("N");
        return Path.Combine(Path.GetTempPath(), $"voxelforge-eval-mcp-{run.CaseId}-{run.VariantId}-{run.Trial}-{suffix}");
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (path is null || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct BenchmarkToolLoopResult(int LlmRounds, int FailedToolCallCount);
}

public sealed class BenchmarkCompletionServiceResult
{
    public required bool Success { get; init; }
    public ICompletionService? CompletionService { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class BenchmarkCompletionServiceFactory
{
    public BenchmarkCompletionServiceResult Create(BenchmarkPlannedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.Equals(run.Provider, "fake", StringComparison.Ordinal)
            || string.Equals(run.Provider, "fixture", StringComparison.Ordinal)
            || string.Equals(run.Provider, "fake-completion", StringComparison.Ordinal))
        {
            return new BenchmarkCompletionServiceResult
            {
                Success = true,
                CompletionService = new FakeBenchmarkCompletionService(),
            };
        }

        return new BenchmarkCompletionServiceResult
        {
            Success = false,
            ErrorMessage = $"Provider '{run.Provider}' is not configured for local benchmark execution. Use provider 'fake' for deterministic fixture runs or add a local provider binding outside committed runsets.",
        };
    }
}

public sealed class FakeBenchmarkCompletionService : ICompletionService
{
    private int _requestCount;

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        _requestCount++;
        if (_requestCount == 1)
        {
            return Task.FromResult(new CompletionResponse
            {
                TextContent = "I will create a small deterministic voxel fixture with the MCP primitive tool.",
                ToolCalls =
                [
                    new ToolCall
                    {
                        Id = "fake-call-1",
                        Name = "apply_voxel_primitives",
                        Arguments = JsonSerializer.SerializeToElement(new
                        {
                            primitives = new object[]
                            {
                                new
                                {
                                    kind = "box",
                                    palette_index = 1,
                                    from = new { x = 0, y = 0, z = 0 },
                                    to = new { x = 1, y = 1, z = 1 },
                                    mode = "filled",
                                },
                            },
                        }),
                    },
                ],
                StopReason = "tool_use",
            });
        }

        return Task.FromResult(new CompletionResponse
        {
            TextContent = "Done: generated a deterministic 2x2x2 voxel block.",
            StopReason = "end_turn",
        });
    }
}

public sealed class BenchmarkMcpToolCatalog
{
    private BenchmarkMcpToolCatalog(
        VoxelForgeMcpSession session,
        string projectDirectory,
        BenchmarkRequiredMcpTools requiredTools,
        IReadOnlyList<IVoxelForgeMcpTool> tools)
    {
        Session = session;
        ProjectDirectory = projectDirectory;
        RequiredTools = requiredTools;
        Tools = tools;
    }

    public VoxelForgeMcpSession Session { get; }
    public string ProjectDirectory { get; }
    public BenchmarkRequiredMcpTools RequiredTools { get; }
    public IReadOnlyList<IVoxelForgeMcpTool> Tools { get; }

    public static BenchmarkMcpToolCatalog Create(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        Directory.CreateDirectory(projectDirectory);
        var loggerFactory = NullLoggerFactory.Instance;
        var config = new EditorConfigState();
        var options = new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory };
        var session = new VoxelForgeMcpSession(config, loggerFactory);
        var voxelEditingService = new VoxelEditingService();
        var lifecycleService = new ProjectLifecycleService(loggerFactory);
        var pathResolver = new ModelPathResolver(options);
        var mutationIntentService = new VoxelMutationIntentService();
        var primitiveGenerationService = new VoxelPrimitiveGenerationService();
        var queryService = new VoxelQueryService();
        var applicationService = new LlmToolApplicationService(voxelEditingService);
        var paletteMaterialService = new PaletteMaterialService();

        var newModel = new NewModelMcpTool(session, loggerFactory, config);
        var loadModel = new LoadModelMcpTool(session, lifecycleService, pathResolver);
        var saveModel = new SaveModelMcpTool(session, lifecycleService, pathResolver);
        var setPaletteEntry = new SetPaletteEntryMcpTool(session, paletteMaterialService);
        var setGridHint = new SetGridHintMcpTool(session, voxelEditingService);
        var getModelInfo = new GetModelInfoMcpTool(new GetModelInfoHandler(queryService), session, applicationService);
        var setVoxels = new SetVoxelsMcpTool(new SetVoxelsHandler(mutationIntentService), session, applicationService);
        var removeVoxels = new RemoveVoxelsMcpTool(new RemoveVoxelsHandler(mutationIntentService), session, applicationService);
        var applyPrimitives = new ApplyVoxelPrimitivesMcpTool(new ApplyVoxelPrimitivesHandler(primitiveGenerationService), session, applicationService);

        var required = new BenchmarkRequiredMcpTools(newModel, loadModel, saveModel, setPaletteEntry);
        IVoxelForgeMcpTool[] tools =
        [
            newModel,
            loadModel,
            saveModel,
            setPaletteEntry,
            getModelInfo,
            setVoxels,
            removeVoxels,
            applyPrimitives,
            setGridHint,
        ];
        return new BenchmarkMcpToolCatalog(session, pathResolver.ProjectDirectory, required, tools);
    }
}

public sealed class BenchmarkRequiredMcpTools
{
    public BenchmarkRequiredMcpTools(
        IVoxelForgeMcpTool newModel,
        IVoxelForgeMcpTool loadModel,
        IVoxelForgeMcpTool saveModel,
        IVoxelForgeMcpTool setPaletteEntry)
    {
        NewModel = newModel;
        LoadModel = loadModel;
        SaveModel = saveModel;
        SetPaletteEntry = setPaletteEntry;
    }

    public IVoxelForgeMcpTool NewModel { get; }
    public IVoxelForgeMcpTool LoadModel { get; }
    public IVoxelForgeMcpTool SaveModel { get; }
    public IVoxelForgeMcpTool SetPaletteEntry { get; }
}

public sealed class BenchmarkTranscriptRecorder
{
    private readonly StringBuilder _conversation = new();
    private readonly StringBuilder _toolCalls = new();
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private int _conversationIndex;
    private int _toolCallIndex;
    private int _setupToolCallNumber;

    public int ToolCallCount => _toolCallIndex;
    public int FailedToolCallCount { get; private set; }
    public int LlmRounds { get; set; }
    public int NextSetupToolCallNumber => ++_setupToolCallNumber;
    public string ConversationJsonl => _conversation.ToString();
    public string ToolCallsJsonl => _toolCalls.ToString();
    public string StdoutLog => _stdout.ToString();
    public string StderrLog => _stderr.ToString();

    public void RecordSystem(string content)
    {
        WriteConversation(new ConversationEntry
        {
            Index = NextConversationIndex(),
            Role = "system",
            Content = content,
            TimestampUtc = Timestamp(),
        });
    }

    public void RecordUser(string content)
    {
        WriteConversation(new ConversationEntry
        {
            Index = NextConversationIndex(),
            Role = "user",
            Content = content,
            TimestampUtc = Timestamp(),
        });
    }

    public void RecordAssistant(string? content, IReadOnlyList<ToolCall> toolCalls)
    {
        string[] ids = new string[toolCalls.Count];
        for (int i = 0; i < toolCalls.Count; i++)
            ids[i] = toolCalls[i].Id;

        WriteConversation(new ConversationEntry
        {
            Index = NextConversationIndex(),
            Role = "assistant",
            Content = content,
            ToolCallIds = ids.Length == 0 ? null : ids,
            TimestampUtc = Timestamp(),
        });
    }

    public void RecordTool(string toolCallId, string name, bool ok, string content)
    {
        WriteConversation(new ConversationEntry
        {
            Index = NextConversationIndex(),
            Role = "tool",
            ToolCallId = toolCallId,
            Name = name,
            Ok = ok,
            Content = content,
            TimestampUtc = Timestamp(),
        });
    }

    public void RecordToolCall(int round, ToolCall call, bool ok, string resultSummary, long durationMs)
    {
        _toolCallIndex++;
        if (!ok)
            FailedToolCallCount++;

        WriteToolCall(new ToolCallTranscriptEntry
        {
            Index = _toolCallIndex,
            Round = round,
            ToolCallId = call.Id,
            Name = call.Name,
            Arguments = JsonSerializer.Deserialize<JsonElement>(call.Arguments.GetRawText()),
            Ok = ok,
            ResultSummary = Summarize(resultSummary),
            DurationMs = durationMs,
        });
    }

    public void RecordLogLine(string stream, string line)
    {
        if (string.Equals(stream, "stderr", StringComparison.Ordinal))
            _stderr.AppendLine(line);
        else
            _stdout.AppendLine(line);
    }

    private int NextConversationIndex()
    {
        _conversationIndex++;
        return _conversationIndex;
    }

    private static DateTimeOffset Timestamp()
    {
        return DateTimeOffset.UtcNow;
    }

    private void WriteConversation(ConversationEntry entry)
    {
        _conversation.AppendLine(JsonSerializer.Serialize(entry, BenchmarkJson.JsonlOptions));
    }

    private void WriteToolCall(ToolCallTranscriptEntry entry)
    {
        _toolCalls.AppendLine(JsonSerializer.Serialize(entry, BenchmarkJson.JsonlOptions));
    }

    private static string Summarize(string value)
    {
        if (value.Length <= 200)
            return value;
        return value[..200] + "...";
    }

    private sealed class ConversationEntry
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }

        [JsonPropertyName("tool_call_ids")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? ToolCallIds { get; init; }

        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId { get; init; }

        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; init; }

        [JsonPropertyName("ok")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Ok { get; init; }

        [JsonPropertyName("timestamp_utc")]
        public DateTimeOffset TimestampUtc { get; init; }
    }

    private sealed class ToolCallTranscriptEntry
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("round")]
        public int Round { get; init; }

        [JsonPropertyName("tool_call_id")]
        public required string ToolCallId { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("arguments")]
        public JsonElement Arguments { get; init; }

        [JsonPropertyName("ok")]
        public bool Ok { get; init; }

        [JsonPropertyName("result_summary")]
        public required string ResultSummary { get; init; }

        [JsonPropertyName("duration_ms")]
        public long DurationMs { get; init; }
    }
}
