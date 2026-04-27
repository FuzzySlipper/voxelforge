using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;
using VoxelForge.Core.Services;

namespace VoxelForge.Evaluation;

public sealed class StdioCommandTransportSession
{
    public StdioCommandTransportSession(CommandRouter router, CommandContext context, EditorDocumentState document, UndoStack undoStack)
    {
        Router = router;
        Context = context;
        Document = document;
        UndoStack = undoStack;
    }

    public CommandRouter Router { get; }
    public CommandContext Context { get; }
    public EditorDocumentState Document { get; }
    public UndoStack UndoStack { get; }
}

public interface IStdioCommandTransport : IDisposable
{
    StdioCommandTransportSession Session { get; }
    Task<StdioCommandResponse> SendAsync(StdioCommandRequest request, CancellationToken cancellationToken);
}

public interface IStdioCommandTransportFactory
{
    IStdioCommandTransport Create(string projectDirectory);
}

public sealed class InProcessStdioCommandTransportFactory : IStdioCommandTransportFactory
{
    public IStdioCommandTransport Create(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        Directory.CreateDirectory(projectDirectory);
        return new InProcessStdioCommandTransport(CreateSession());
    }

    private static StdioCommandTransportSession CreateSession()
    {
        ILoggerFactory loggerFactory = NullLoggerFactory.Instance;
        var events = new ApplicationEventDispatcher();
        var document = new EditorDocumentState(
            new VoxelModel(NullLogger<VoxelModel>.Instance),
            new LabelIndex(NullLogger<LabelIndex>.Instance));
        var undoStack = new UndoStack(
            new UndoHistoryState(100),
            loggerFactory.CreateLogger<UndoStack>(),
            events);
        var context = new CommandContext
        {
            Document = document,
            UndoStack = undoStack,
            Events = events,
            Mode = ExecutionMode.Stdio,
        };
        CommandRouter router = CreateRouter(loggerFactory);
        return new StdioCommandTransportSession(router, context, document, undoStack);
    }

    private static CommandRouter CreateRouter(ILoggerFactory loggerFactory)
    {
        var voxelEditingService = new VoxelEditingService();
        var voxelQueryService = new VoxelQueryService();
        var paletteMaterialService = new PaletteMaterialService();
        var projectLifecycleService = new ProjectLifecycleService(loggerFactory);
        IConsoleCommand[] commands =
        [
            new DescribeCommand(voxelQueryService),
            new SetVoxelConsoleCommand(voxelEditingService),
            new RemoveVoxelConsoleCommand(voxelEditingService),
            new ClearCommand(voxelEditingService),
            new GridCommand(voxelEditingService),
            new GetVoxelCommand(voxelQueryService),
            new CountCommand(voxelQueryService),
            new PaletteCommand(paletteMaterialService),
            new SaveCommand(projectLifecycleService),
            new LoadCommand(projectLifecycleService),
            new UndoCommand(),
            new RedoCommand(),
        ];
        return new CommandRouter(commands, loggerFactory.CreateLogger<CommandRouter>());
    }
}

public sealed class InProcessStdioCommandTransport : IStdioCommandTransport
{
    public InProcessStdioCommandTransport(StdioCommandTransportSession session)
    {
        Session = session;
    }

    public StdioCommandTransportSession Session { get; }

    public Task<StdioCommandResponse> SendAsync(StdioCommandRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Task.FromResult(new StdioCommandResponse
            {
                Ok = false,
                Message = "Missing 'command' field.",
            });
        }

        string[] args = request.Args ?? [];
        CommandResult result = Session.Router.Execute(request.Command, args, Session.Context);
        return Task.FromResult(CreateResponse(result));
    }

    public void Dispose()
    {
    }

    private static StdioCommandResponse CreateResponse(CommandResult result)
    {
        if (result.Data is byte[] imageBytes)
        {
            return new StdioCommandResponse
            {
                Ok = result.Success,
                Message = result.Message,
                Image = Convert.ToBase64String(imageBytes),
            };
        }

        if (result.Data is byte[][] imageArray)
        {
            string[] images = new string[imageArray.Length];
            for (int i = 0; i < imageArray.Length; i++)
                images[i] = Convert.ToBase64String(imageArray[i]);

            return new StdioCommandResponse
            {
                Ok = result.Success,
                Message = result.Message,
                Images = images,
            };
        }

        return new StdioCommandResponse
        {
            Ok = result.Success,
            Message = result.Message,
        };
    }
}

public sealed class StdioCommandBenchmarkBackend : IBenchmarkExecutionBackend
{
    private readonly IStdioCommandTransportFactory _transportFactory;
    private readonly BenchmarkMetricsService _metricsService = new();

    public StdioCommandBenchmarkBackend(IStdioCommandTransportFactory transportFactory)
    {
        _transportFactory = transportFactory;
    }

    public async Task<BenchmarkRunExecutionResult> RunAsync(
        BenchmarkRunExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.PlannedRun);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        var recorder = new StdioTranscriptRecorder();
        string? workDirectory = null;
        IStdioCommandTransport? transport = null;

        try
        {
            workDirectory = CreateWorkDirectory(request.PlannedRun);
            Directory.CreateDirectory(workDirectory);
            transport = _transportFactory.Create(workDirectory);

            await InitializeSessionAsync(transport, request, recorder, cancellationToken).ConfigureAwait(false);
            await ReplayScriptAsync(transport, request, recorder, cancellationToken).ConfigureAwait(false);
            await ExecuteRequestAsync(
                transport,
                recorder,
                new StdioCommandRequest
                {
                    Command = "save",
                    Args = [Path.Combine(workDirectory, "final.vforge")],
                },
                cancellationToken).ConfigureAwait(false);

            DateTimeOffset endedAt = DateTimeOffset.UtcNow;
            return new BenchmarkRunExecutionResult
            {
                Model = transport.Session.Document.Model,
                Labels = transport.Session.Document.Labels,
                Clips = transport.Session.Document.Clips,
                Metadata = new ProjectMetadata { Name = CreateSafeModelName(request.PlannedRun) },
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                Status = recorder.FailedRequestCount == 0 ? "succeeded" : "failed",
                ToolCallCount = recorder.RequestCount,
                FailedToolCallCount = recorder.FailedRequestCount,
                UndoableMutationCount = transport.Session.UndoStack.History.UndoCount,
                LlmRounds = 0,
                ErrorCount = recorder.FailedRequestCount,
                ToolSchemaSha256 = ComputeCommandSchemaSha(transport.Session.Router),
                StdioTranscriptJsonl = recorder.StdioJsonl,
                StdoutLog = recorder.StdoutLog,
                StderrLog = recorder.StderrLog,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DateTimeOffset endedAt = DateTimeOffset.UtcNow;
            recorder.RecordLogLine("stderr", ex.Message);
            VoxelModel fallbackModel = transport?.Session.Document.Model ?? new VoxelModel(NullLogger<VoxelModel>.Instance);
            LabelIndex fallbackLabels = transport?.Session.Document.Labels ?? new LabelIndex(NullLogger<LabelIndex>.Instance);
            return new BenchmarkRunExecutionResult
            {
                Model = fallbackModel,
                Labels = fallbackLabels,
                Clips = transport?.Session.Document.Clips ?? [],
                Metadata = new ProjectMetadata { Name = "failed-stdio-run" },
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                Status = "failed",
                ToolCallCount = recorder.RequestCount,
                FailedToolCallCount = recorder.FailedRequestCount,
                UndoableMutationCount = transport?.Session.UndoStack.History.UndoCount ?? 0,
                LlmRounds = 0,
                ErrorCount = Math.Max(1, recorder.FailedRequestCount),
                ToolSchemaSha256 = transport is null ? null : ComputeCommandSchemaSha(transport.Session.Router),
                StdioTranscriptJsonl = recorder.StdioJsonl,
                StdoutLog = recorder.StdoutLog,
                StderrLog = recorder.StderrLog,
                Failure = new BenchmarkFailureArtifact
                {
                    Phase = "stdio",
                    ExceptionType = ex.GetType().Name,
                    Message = ex.Message,
                },
            };
        }
        finally
        {
            transport?.Dispose();
            TryDeleteDirectory(workDirectory);
        }
    }

    private static async Task InitializeSessionAsync(
        IStdioCommandTransport transport,
        BenchmarkRunExecutionRequest request,
        StdioTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PlannedRun.InitialModel))
        {
            await ExecuteRequestAsync(
                transport,
                recorder,
                new StdioCommandRequest
                {
                    Command = "load",
                    Args = [ResolveInputPath(request.InputRoot, request.PlannedRun.InitialModel)],
                },
                cancellationToken).ConfigureAwait(false);
        }

        await ApplyPaletteFileAsync(transport, request, recorder, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyPaletteFileAsync(
        IStdioCommandTransport transport,
        BenchmarkRunExecutionRequest request,
        StdioTranscriptRecorder recorder,
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
            await ExecuteRequestAsync(
                transport,
                recorder,
                CreatePaletteRequest(entry),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReplayScriptAsync(
        IStdioCommandTransport transport,
        BenchmarkRunExecutionRequest request,
        StdioTranscriptRecorder recorder,
        CancellationToken cancellationToken)
    {
        string scriptPath = ResolveInputPath(request.InputRoot, request.PlannedRun.PromptFile);
        using var reader = new StreamReader(scriptPath);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            StdioCommandRequest? commandRequest;
            try
            {
                commandRequest = JsonSerializer.Deserialize<StdioCommandRequest>(line, StdioCommandJson.Options);
            }
            catch (JsonException ex)
            {
                recorder.RecordInvalidRequest(line, $"Invalid JSON: {ex.Message}");
                continue;
            }

            if (commandRequest is null || string.IsNullOrWhiteSpace(commandRequest.Command))
            {
                recorder.RecordInvalidRequest(line, "Missing 'command' field.");
                continue;
            }

            await ExecuteRequestAsync(transport, recorder, Normalize(commandRequest), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteRequestAsync(
        IStdioCommandTransport transport,
        StdioTranscriptRecorder recorder,
        StdioCommandRequest request,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        StdioCommandResponse response = await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        recorder.RecordExchange(request, response, stopwatch.ElapsedMilliseconds);
    }

    private static StdioCommandRequest Normalize(StdioCommandRequest request)
    {
        return new StdioCommandRequest
        {
            Command = request.Command,
            Args = request.Args ?? [],
        };
    }

    private static StdioCommandRequest CreatePaletteRequest(JsonElement entry)
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
        return new StdioCommandRequest
        {
            Command = "palette",
            Args =
            [
                "add",
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                name,
                red.ToString(System.Globalization.CultureInfo.InvariantCulture),
                green.ToString(System.Globalization.CultureInfo.InvariantCulture),
                blue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                alpha.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ],
        };
    }

    private static int ReadColor(JsonElement entry, string shortName, string longName, int defaultValue = 0)
    {
        if (entry.TryGetProperty(shortName, out JsonElement shortElement) && shortElement.ValueKind == JsonValueKind.Number)
            return shortElement.GetInt32();
        if (entry.TryGetProperty(longName, out JsonElement longElement) && longElement.ValueKind == JsonValueKind.Number)
            return longElement.GetInt32();
        return defaultValue;
    }

    private string ComputeCommandSchemaSha(CommandRouter router)
    {
        var definitionsByName = new SortedDictionary<string, StdioCommandDefinition>(StringComparer.Ordinal);
        foreach (IConsoleCommand command in router.Commands.Values)
        {
            if (definitionsByName.ContainsKey(command.Name))
                continue;

            definitionsByName.Add(command.Name, new StdioCommandDefinition
            {
                Name = command.Name,
                Aliases = command.Aliases,
                Help = command.HelpText,
            });
        }

        var definitions = new List<StdioCommandDefinition>(definitionsByName.Values);
        string json = JsonSerializer.Serialize(definitions, BenchmarkJson.WriteIndentedOptions);
        return _metricsService.ComputeTextSha256(json);
    }

    private static string ResolveInputPath(string inputRoot, string path)
    {
        return Path.GetFullPath(Path.Combine(inputRoot, path));
    }

    private static string CreateWorkDirectory(BenchmarkPlannedRun run)
    {
        string suffix = Guid.NewGuid().ToString("N");
        return Path.Combine(Path.GetTempPath(), $"voxelforge-eval-stdio-{run.CaseId}-{run.VariantId}-{run.Trial}-{suffix}");
    }

    private static string CreateSafeModelName(BenchmarkPlannedRun run)
    {
        return $"{run.CaseId}-{run.VariantId}-trial-{run.Trial}";
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

    private sealed class StdioCommandDefinition
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("aliases")]
        public required IReadOnlyList<string> Aliases { get; init; }

        [JsonPropertyName("help")]
        public required string Help { get; init; }
    }
}

public sealed class StdioTranscriptRecorder
{
    private readonly StringBuilder _stdio = new();
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private int _index;

    public int RequestCount => _index;
    public int FailedRequestCount { get; private set; }
    public string StdioJsonl => _stdio.ToString();
    public string StdoutLog => _stdout.ToString();
    public string StderrLog => _stderr.ToString();

    public void RecordExchange(StdioCommandRequest request, StdioCommandResponse response, long durationMs)
    {
        _index++;
        if (!response.Ok)
            FailedRequestCount++;

        WriteEntry(new StdioTranscriptEntry
        {
            Index = _index,
            TimestampUtc = DateTimeOffset.UtcNow,
            Request = request,
            Response = response,
            DurationMs = durationMs,
        });
        RecordStdout(response);
    }

    public void RecordInvalidRequest(string rawRequest, string message)
    {
        _index++;
        FailedRequestCount++;
        var response = new StdioCommandResponse
        {
            Ok = false,
            Message = message,
        };
        WriteEntry(new StdioTranscriptEntry
        {
            Index = _index,
            TimestampUtc = DateTimeOffset.UtcNow,
            RawRequest = rawRequest,
            Response = response,
            DurationMs = 0,
        });
        RecordStdout(response);
    }

    public void RecordLogLine(string stream, string line)
    {
        if (string.Equals(stream, "stderr", StringComparison.Ordinal))
            _stderr.AppendLine(line);
        else
            _stdout.AppendLine(line);
    }

    private void RecordStdout(StdioCommandResponse response)
    {
        _stdout.AppendLine(JsonSerializer.Serialize(response, StdioCommandJson.Options));
    }

    private void WriteEntry(StdioTranscriptEntry entry)
    {
        _stdio.AppendLine(JsonSerializer.Serialize(entry, BenchmarkJson.JsonlOptions));
    }

    private sealed class StdioTranscriptEntry
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("timestamp_utc")]
        public DateTimeOffset TimestampUtc { get; init; }

        [JsonPropertyName("request")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public StdioCommandRequest? Request { get; init; }

        [JsonPropertyName("raw_request")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? RawRequest { get; init; }

        [JsonPropertyName("response")]
        public required StdioCommandResponse Response { get; init; }

        [JsonPropertyName("duration_ms")]
        public long DurationMs { get; init; }
    }
}

internal static class StdioCommandJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
