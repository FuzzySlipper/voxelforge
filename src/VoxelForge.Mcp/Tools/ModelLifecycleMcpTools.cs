using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;

namespace VoxelForge.Mcp.Tools;

public abstract class ModelLifecycleMcpToolBase : IVoxelForgeMcpTool
{
    private readonly JsonElement _inputSchema;

    protected ModelLifecycleMcpToolBase(VoxelForgeMcpSession session, string name, string description, JsonElement inputSchema, bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Session = session;
        Name = name;
        Description = description;
        _inputSchema = inputSchema;
        IsReadOnly = isReadOnly;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema => _inputSchema;

    public bool IsReadOnly { get; }

    protected VoxelForgeMcpSession Session { get; }

    public abstract McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken);

    protected static McpToolInvocationResult Fail(string message)
    {
        return new McpToolInvocationResult
        {
            Success = false,
            Message = message,
        };
    }

    protected static McpToolInvocationResult Ok(string message)
    {
        return new McpToolInvocationResult
        {
            Success = true,
            Message = message,
        };
    }

    protected static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    protected static bool TryReadRequiredString(JsonElement arguments, string propertyName, out string value, out string errorMessage)
    {
        value = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required string property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Property '{propertyName}' must be a string.";
            return false;
        }

        value = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = $"Property '{propertyName}' cannot be empty.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadOptionalString(JsonElement arguments, string propertyName, out string? value, out string errorMessage)
    {
        value = null;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Property '{propertyName}' must be a string when provided.";
            return false;
        }

        value = element.GetString();
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadRequiredInt(JsonElement arguments, string propertyName, out int value, out string errorMessage)
    {
        value = 0;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required integer property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            errorMessage = $"Property '{propertyName}' must be an integer.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadOptionalInt(JsonElement arguments, string propertyName, out int value, out bool hasValue, out string errorMessage)
    {
        value = 0;
        hasValue = false;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            errorMessage = $"Property '{propertyName}' must be an integer when provided.";
            return false;
        }

        hasValue = true;
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadOptionalBool(JsonElement arguments, string propertyName, bool defaultValue, out bool value, out string errorMessage)
    {
        value = defaultValue;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
        {
            errorMessage = $"Property '{propertyName}' must be a boolean when provided.";
            return false;
        }

        value = element.GetBoolean();
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadByte(JsonElement arguments, string propertyName, out byte value, out string errorMessage)
    {
        value = 0;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required byte property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int intValue) || intValue < byte.MinValue || intValue > byte.MaxValue)
        {
            errorMessage = $"Property '{propertyName}' must be an integer from 0 to 255.";
            return false;
        }

        value = (byte)intValue;
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadOptionalByte(JsonElement arguments, string propertyName, byte defaultValue, out byte value, out string errorMessage)
    {
        value = defaultValue;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int intValue) || intValue < byte.MinValue || intValue > byte.MaxValue)
        {
            errorMessage = $"Property '{propertyName}' must be an integer from 0 to 255 when provided.";
            return false;
        }

        value = (byte)intValue;
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class ModelPathResolver
{
    private readonly VoxelForgeMcpOptions _options;

    public ModelPathResolver(VoxelForgeMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string ProjectDirectory => _options.GetResolvedProjectDirectory();

    public bool TryResolveModelPath(string name, out string path, out string errorMessage)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = "Model name cannot be empty.";
            return false;
        }

        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar) || name.Contains("..", StringComparison.Ordinal))
        {
            errorMessage = "Model name must be a file name within the configured project directory.";
            return false;
        }

        var fileName = Path.HasExtension(name) ? name : name + ".vforge";
        path = Path.Combine(ProjectDirectory, fileName);
        errorMessage = string.Empty;
        return true;
    }

    public static string NormalizeModelName(string name)
    {
        return Path.GetFileNameWithoutExtension(name.Trim());
    }
}

public sealed class NewModelMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly EditorConfigState _config;

    public NewModelMcpTool(VoxelForgeMcpSession session, ILoggerFactory loggerFactory, EditorConfigState config)
        : base(
            session,
            "new_model",
            "Create a new empty in-memory model with name, grid hint, and optional initial palette entries.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" },
                    "grid_hint": { "type": "integer", "minimum": 1, "maximum": 256 },
                    "palette_entries": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "index": { "type": "integer", "minimum": 1, "maximum": 255 },
                                "name": { "type": "string" },
                                "r": { "type": "integer", "minimum": 0, "maximum": 255 },
                                "g": { "type": "integer", "minimum": 0, "maximum": 255 },
                                "b": { "type": "integer", "minimum": 0, "maximum": 255 },
                                "a": { "type": "integer", "minimum": 0, "maximum": 255 }
                            },
                            "required": ["index", "name", "r", "g", "b"]
                        }
                    }
                },
                "required": ["name"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(config);
        _loggerFactory = loggerFactory;
        _config = config;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "name", out var name, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadOptionalInt(arguments, "grid_hint", out int gridHint, out bool hasGridHint, out errorMessage))
            return Fail(errorMessage);
        if (hasGridHint && (gridHint < 1 || gridHint > 256))
            return Fail("Property 'grid_hint' must be between 1 and 256.");
        if (!TryReadPaletteEntries(arguments, out var paletteEntries, out errorMessage))
            return Fail(errorMessage);

        var model = new VoxelModel(_loggerFactory.CreateLogger<VoxelModel>())
        {
            GridHint = hasGridHint ? gridHint : _config.DefaultGridHint,
        };
        for (int i = 0; i < paletteEntries.Count; i++)
            model.Palette.Set(paletteEntries[i].Index, paletteEntries[i].Material);

        var labels = new LabelIndex(_loggerFactory.CreateLogger<LabelIndex>());
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Session.UndoStack.Execute(new ReplaceDocumentCommand(
                Session.Document,
                model,
                labels,
                [],
                $"New model '{name}'"));
            Session.CurrentModelName = ModelPathResolver.NormalizeModelName(name);

            var applicationEvents = new IApplicationEvent[]
            {
                new VoxelModelChangedEvent(
                    VoxelModelChangeKind.ProjectLoaded,
                    $"Created new model '{Session.CurrentModelName}'",
                    0),
                new PaletteChangedEvent(
                    PaletteChangeKind.EntriesChanged,
                    "Initialized model palette",
                    null,
                    model.Palette.Count),
            };
            Session.Events.PublishAll(applicationEvents);
        }

        return Ok($"Created new model '{ModelPathResolver.NormalizeModelName(name)}' with grid hint {model.GridHint} and {model.Palette.Count} palette entries.");
    }

    private static bool TryReadPaletteEntries(JsonElement arguments, out IReadOnlyList<InitialPaletteEntry> entries, out string errorMessage)
    {
        var result = new List<InitialPaletteEntry>();
        entries = result;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("palette_entries", out var arrayElement) || arrayElement.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "Property 'palette_entries' must be an array when provided.";
            return false;
        }

        foreach (var entryElement in arrayElement.EnumerateArray())
        {
            if (!TryReadByte(entryElement, "index", out byte index, out errorMessage) ||
                !TryReadRequiredString(entryElement, "name", out var materialName, out errorMessage) ||
                !TryReadByte(entryElement, "r", out byte red, out errorMessage) ||
                !TryReadByte(entryElement, "g", out byte green, out errorMessage) ||
                !TryReadByte(entryElement, "b", out byte blue, out errorMessage) ||
                !TryReadOptionalByte(entryElement, "a", 255, out byte alpha, out errorMessage))
            {
                return false;
            }

            if (index == 0)
            {
                errorMessage = "Palette index 0 is reserved for air.";
                return false;
            }

            result.Add(new InitialPaletteEntry(index, new MaterialDef
            {
                Name = materialName,
                Color = new RgbaColor(red, green, blue, alpha),
            }));
        }

        errorMessage = string.Empty;
        return true;
    }

    private readonly record struct InitialPaletteEntry(byte Index, MaterialDef Material);
}

public sealed class NewModelServerTool : VoxelForgeMcpServerTool
{
    public NewModelServerTool(NewModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class SaveModelMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ProjectLifecycleService _projectLifecycleService;
    private readonly ModelPathResolver _pathResolver;

    public SaveModelMcpTool(VoxelForgeMcpSession session, ProjectLifecycleService projectLifecycleService, ModelPathResolver pathResolver)
        : base(
            session,
            "save_model",
            "Save the current in-memory model to a .vforge file in the configured project directory.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "description": "Optional file/model name. Defaults to the current model name." }
                }
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(projectLifecycleService);
        ArgumentNullException.ThrowIfNull(pathResolver);
        _projectLifecycleService = projectLifecycleService;
        _pathResolver = pathResolver;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadOptionalString(arguments, "name", out var name, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string targetName = string.IsNullOrWhiteSpace(name) ? Session.CurrentModelName : name;
            if (!_pathResolver.TryResolveModelPath(targetName, out var path, out errorMessage))
                return Fail(errorMessage);

            var result = _projectLifecycleService.Save(Session.Document, Session.Events, new SaveProjectRequest(path));
            if (result.Success)
                Session.CurrentModelName = ModelPathResolver.NormalizeModelName(targetName);
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class SaveModelServerTool : VoxelForgeMcpServerTool
{
    public SaveModelServerTool(SaveModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class PublishPreviewMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ModelPathResolver _pathResolver;
    private readonly ILoggerFactory _loggerFactory;

    public PublishPreviewMcpTool(VoxelForgeMcpSession session, ModelPathResolver pathResolver, ILoggerFactory loggerFactory)
        : base(
            session,
            "publish_preview",
            "Atomically publish the current MCP session as a .vforge preview snapshot for a watching GUI.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Optional preview file name within the configured project directory. Defaults to mcp-preview."
                    },
                    "write_manifest": {
                        "type": "boolean",
                        "description": "Whether to write a sidecar .preview.json manifest. Defaults to true."
                    }
                }
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _pathResolver = pathResolver;
        _loggerFactory = loggerFactory;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadOptionalString(arguments, "name", out var name, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadOptionalBool(arguments, "write_manifest", true, out bool writeManifest, out errorMessage))
            return Fail(errorMessage);

        string targetName = string.IsNullOrWhiteSpace(name) ? "mcp-preview" : name;
        if (!_pathResolver.TryResolveModelPath(targetName, out var path, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _pathResolver.ProjectDirectory);
                var serializer = new ProjectSerializer(_loggerFactory);
                var metadata = new ProjectMetadata { Name = Session.CurrentModelName };
                string json = serializer.Serialize(Session.Document.Model, Session.Document.Labels, Session.Document.Clips, metadata);
                AtomicWriteAllText(path, json);

                string? manifestPath = null;
                if (writeManifest)
                {
                    manifestPath = Path.ChangeExtension(path, ".preview.json");
                    string manifestJson = SerializeJson(new Dictionary<string, object?>
                    {
                        ["schema"] = "voxelforge.preview_manifest",
                        ["schema_version"] = 1,
                        ["source"] = "VoxelForge.Mcp",
                        ["tool"] = Name,
                        ["model_name"] = Session.CurrentModelName,
                        ["preview_name"] = ModelPathResolver.NormalizeModelName(targetName),
                        ["model_path"] = path,
                        ["model_file"] = Path.GetFileName(path),
                        ["updated_at_utc"] = DateTimeOffset.UtcNow,
                        ["byte_count"] = json.Length,
                        ["voxel_count"] = Session.Document.Model.GetVoxelCount(),
                        ["region_count"] = Session.Document.Labels.Regions.Count,
                        ["clip_count"] = Session.Document.Clips.Count,
                    });
                    AtomicWriteAllText(manifestPath, manifestJson);
                }

                return Ok(SerializeJson(new Dictionary<string, object?>
                {
                    ["message"] = $"Published preview to {path} ({json.Length} bytes)",
                    ["path"] = path,
                    ["manifest_path"] = manifestPath,
                    ["byte_count"] = json.Length,
                    ["voxel_count"] = Session.Document.Model.GetVoxelCount(),
                    ["region_count"] = Session.Document.Labels.Regions.Count,
                    ["clip_count"] = Session.Document.Clips.Count,
                }));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return Fail($"Failed to publish preview: {ex.Message}");
            }
        }
    }

    private static void AtomicWriteAllText(string path, string content)
    {
        string directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tempPath, content);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}

public sealed class PublishPreviewServerTool : VoxelForgeMcpServerTool
{
    public PublishPreviewServerTool(PublishPreviewMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class LoadModelMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ProjectLifecycleService _projectLifecycleService;
    private readonly ModelPathResolver _pathResolver;

    public LoadModelMcpTool(VoxelForgeMcpSession session, ProjectLifecycleService projectLifecycleService, ModelPathResolver pathResolver)
        : base(
            session,
            "load_model",
            "Load a .vforge file by name from the configured project directory into the MCP session.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(projectLifecycleService);
        ArgumentNullException.ThrowIfNull(pathResolver);
        _projectLifecycleService = projectLifecycleService;
        _pathResolver = pathResolver;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "name", out var name, out var errorMessage))
            return Fail(errorMessage);
        if (!_pathResolver.TryResolveModelPath(name, out var path, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _projectLifecycleService.Load(Session.Document, Session.UndoStack, Session.Events, new LoadProjectRequest(path));
            if (result.Success)
                Session.CurrentModelName = ModelPathResolver.NormalizeModelName(name);
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class LoadModelServerTool : VoxelForgeMcpServerTool
{
    public LoadModelServerTool(LoadModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ListModelsMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ModelPathResolver _pathResolver;

    public ListModelsMcpTool(VoxelForgeMcpSession session, ModelPathResolver pathResolver)
        : base(
            session,
            "list_models",
            "List available .vforge model files in the configured project directory.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: true)
    {
        ArgumentNullException.ThrowIfNull(pathResolver);
        _pathResolver = pathResolver;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = _pathResolver.ProjectDirectory;
        Directory.CreateDirectory(directory);
        var files = new List<Dictionary<string, object?>>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.vforge", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            files.Add(new Dictionary<string, object?>
            {
                ["name"] = Path.GetFileNameWithoutExtension(path),
                ["fileName"] = Path.GetFileName(path),
                ["byteCount"] = info.Length,
                ["lastWriteUtc"] = info.LastWriteTimeUtc,
            });
        }
        files.Sort(CompareModelFiles);

        return Ok(SerializeJson(new Dictionary<string, object?>
        {
            ["projectDirectory"] = directory,
            ["models"] = files,
            ["count"] = files.Count,
        }));
    }

    private static int CompareModelFiles(Dictionary<string, object?> left, Dictionary<string, object?> right)
    {
        return string.CompareOrdinal((string?)left["fileName"], (string?)right["fileName"]);
    }
}

public sealed class ListModelsServerTool : VoxelForgeMcpServerTool
{
    public ListModelsServerTool(ListModelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ListPaletteMcpTool : ModelLifecycleMcpToolBase
{
    private readonly PaletteMaterialService _paletteMaterialService;

    public ListPaletteMcpTool(VoxelForgeMcpSession session, PaletteMaterialService paletteMaterialService)
        : base(
            session,
            "list_palette",
            "List palette entries for the current model.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: true)
    {
        ArgumentNullException.ThrowIfNull(paletteMaterialService);
        _paletteMaterialService = paletteMaterialService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _paletteMaterialService.ListMaterials(Session.Document.Model.Palette);
            var entries = new List<Dictionary<string, object?>>();
            if (result.Data is not null)
            {
                for (int i = 0; i < result.Data.Count; i++)
                {
                    var entry = result.Data[i];
                    entries.Add(new Dictionary<string, object?>
                    {
                        ["index"] = entry.PaletteIndex,
                        ["name"] = entry.Name,
                        ["r"] = entry.Color.R,
                        ["g"] = entry.Color.G,
                        ["b"] = entry.Color.B,
                        ["a"] = entry.Color.A,
                    });
                }
            }

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["entries"] = entries,
                ["count"] = entries.Count,
            }));
        }
    }
}

public sealed class ListPaletteServerTool : VoxelForgeMcpServerTool
{
    public ListPaletteServerTool(ListPaletteMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class SetPaletteEntryMcpTool : ModelLifecycleMcpToolBase
{
    private readonly PaletteMaterialService _paletteMaterialService;

    public SetPaletteEntryMcpTool(VoxelForgeMcpSession session, PaletteMaterialService paletteMaterialService)
        : base(
            session,
            "set_palette_entry",
            "Define or update a palette entry with index, name, and RGBA color.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "minimum": 1, "maximum": 255 },
                    "name": { "type": "string" },
                    "r": { "type": "integer", "minimum": 0, "maximum": 255 },
                    "g": { "type": "integer", "minimum": 0, "maximum": 255 },
                    "b": { "type": "integer", "minimum": 0, "maximum": 255 },
                    "a": { "type": "integer", "minimum": 0, "maximum": 255 }
                },
                "required": ["index", "name", "r", "g", "b"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(paletteMaterialService);
        _paletteMaterialService = paletteMaterialService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadByte(arguments, "index", out byte index, out var errorMessage) ||
            !TryReadRequiredString(arguments, "name", out var name, out errorMessage) ||
            !TryReadByte(arguments, "r", out byte red, out errorMessage) ||
            !TryReadByte(arguments, "g", out byte green, out errorMessage) ||
            !TryReadByte(arguments, "b", out byte blue, out errorMessage) ||
            !TryReadOptionalByte(arguments, "a", 255, out byte alpha, out errorMessage))
        {
            return Fail(errorMessage);
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _paletteMaterialService.AddMaterial(
                Session.Document.Model,
                Session.UndoStack,
                Session.Events,
                new AddPaletteMaterialRequest(index, name, red, green, blue, alpha));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class SetPaletteEntryServerTool : VoxelForgeMcpServerTool
{
    public SetPaletteEntryServerTool(SetPaletteEntryMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class SetGridHintMcpTool : ModelLifecycleMcpToolBase
{
    private readonly VoxelEditingService _voxelEditingService;

    public SetGridHintMcpTool(VoxelForgeMcpSession session, VoxelEditingService voxelEditingService)
        : base(
            session,
            "set_grid_hint",
            "Set the advisory grid resolution for the current model.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "size": { "type": "integer", "minimum": 1, "maximum": 256 }
                },
                "required": ["size"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(voxelEditingService);
        _voxelEditingService = voxelEditingService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredInt(arguments, "size", out int size, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _voxelEditingService.SetGridHint(
                Session.Document,
                Session.UndoStack,
                Session.Events,
                new SetGridHintRequest(size));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class SetGridHintServerTool : VoxelForgeMcpServerTool
{
    public SetGridHintServerTool(SetGridHintMcpTool tool)
        : base(tool)
    {
    }
}
