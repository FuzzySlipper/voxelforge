using System.Globalization;
using System.Text.Json;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tools;

public sealed class LoadReferenceModelMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ReferenceAssetService _referenceAssetService;

    public LoadReferenceModelMcpTool(VoxelForgeMcpSession session, ReferenceAssetService referenceAssetService)
        : base(
            session,
            "load_reference_model",
            "Load a 3D reference model file (FBX, OBJ, GLTF, etc.) into the MCP session.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "path": { "type": "string", "description": "Absolute or relative path to the model file." }
                },
                "required": ["path"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(referenceAssetService);
        _referenceAssetService = referenceAssetService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "path", out var path, out var errorMessage))
            return Fail(errorMessage);

        if (string.IsNullOrWhiteSpace(path))
            return Fail("Path cannot be empty.");

        path = Path.GetFullPath(path);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _referenceAssetService.LoadModel(
                Session.ReferenceModels,
                Session.Events,
                new LoadReferenceAssetRequest(path));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class LoadReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public LoadReferenceModelServerTool(LoadReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ListReferenceModelsMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ReferenceAssetService _referenceAssetService;

    public ListReferenceModelsMcpTool(VoxelForgeMcpSession session, ReferenceAssetService referenceAssetService)
        : base(
            session,
            "list_reference_models",
            "List loaded reference models with index, file name, format, vertex count, render mode, and visibility.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: true)
    {
        ArgumentNullException.ThrowIfNull(referenceAssetService);
        _referenceAssetService = referenceAssetService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _referenceAssetService.ListModels(Session.ReferenceModels);
            var entries = new List<Dictionary<string, object?>>();
            if (result.Data is not null)
            {
                foreach (var entry in result.Data)
                {
                    entries.Add(new Dictionary<string, object?>
                    {
                        ["index"] = entry.Position,
                        ["file_name"] = entry.FileName,
                        ["format"] = entry.Format,
                        ["total_vertices"] = entry.TotalVertices,
                        ["render_mode"] = entry.RenderMode.ToString().ToLowerInvariant(),
                        ["is_visible"] = entry.IsVisible,
                    });
                }
            }

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["models"] = entries,
                ["count"] = entries.Count,
            }));
        }
    }
}

public sealed class ListReferenceModelsServerTool : VoxelForgeMcpServerTool
{
    public ListReferenceModelsServerTool(ListReferenceModelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class TransformReferenceModelMcpTool : ModelLifecycleMcpToolBase
{
    public TransformReferenceModelMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "transform_reference_model",
            "Translate, rotate, and/or scale a loaded reference model by index.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "x": { "type": "number", "description": "Position X." },
                    "y": { "type": "number", "description": "Position Y." },
                    "z": { "type": "number", "description": "Position Z." },
                    "rx": { "type": "number", "description": "Rotation X in degrees." },
                    "ry": { "type": "number", "description": "Rotation Y in degrees." },
                    "rz": { "type": "number", "description": "Rotation Z in degrees." },
                    "scale": { "type": "number", "description": "Uniform scale factor." }
                },
                "required": ["index", "x", "y", "z"]
            }
            """),
            isReadOnly: false)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadFloat(arguments, "x", out float x, out errorMessage) ||
            !TryReadFloat(arguments, "y", out float y, out errorMessage) ||
            !TryReadFloat(arguments, "z", out float z, out errorMessage))
        {
            return Fail(errorMessage);
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            model.PositionX = x;
            model.PositionY = y;
            model.PositionZ = z;

            if (TryReadOptionalFloat(arguments, "rx", out float rx, out bool hasRx) && hasRx)
                model.RotationX = rx;
            if (TryReadOptionalFloat(arguments, "ry", out float ry, out bool hasRy) && hasRy)
                model.RotationY = ry;
            if (TryReadOptionalFloat(arguments, "rz", out float rz, out bool hasRz) && hasRz)
                model.RotationZ = rz;
            if (TryReadOptionalFloat(arguments, "scale", out float scale, out bool hasScale) && hasScale)
                model.Scale = scale;

            Session.Events.Publish(new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.TransformChanged,
                $"Transformed reference model [{index}]",
                index));

            return Ok($"[{index}] pos=({x},{y},{z}) rot=({model.RotationX},{model.RotationY},{model.RotationZ}) scale={model.Scale}");
        }
    }

    private static bool TryReadFloat(JsonElement arguments, string propertyName, out float value, out string errorMessage)
    {
        value = 0f;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required number property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out value))
        {
            errorMessage = $"Property '{propertyName}' must be a number.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static bool TryReadOptionalFloat(JsonElement arguments, string propertyName, out float value, out bool hasValue)
    {
        value = 0f;
        hasValue = false;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out value))
            return false;

        hasValue = true;
        return true;
    }
}

public sealed class TransformReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public TransformReferenceModelServerTool(TransformReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class RemoveReferenceModelMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ReferenceAssetService _referenceAssetService;

    public RemoveReferenceModelMcpTool(VoxelForgeMcpSession session, ReferenceAssetService referenceAssetService)
        : base(
            session,
            "remove_reference_model",
            "Remove a loaded reference model by index.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." }
                },
                "required": ["index"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(referenceAssetService);
        _referenceAssetService = referenceAssetService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _referenceAssetService.RemoveModel(
                Session.ReferenceModels,
                Session.Events,
                new RemoveReferenceAssetRequest(index));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class RemoveReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public RemoveReferenceModelServerTool(RemoveReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ClearReferenceModelsMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ReferenceAssetService _referenceAssetService;

    public ClearReferenceModelsMcpTool(VoxelForgeMcpSession session, ReferenceAssetService referenceAssetService)
        : base(
            session,
            "clear_reference_models",
            "Remove all loaded reference models.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(referenceAssetService);
        _referenceAssetService = referenceAssetService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _referenceAssetService.ClearModels(Session.ReferenceModels, Session.Events);
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class ClearReferenceModelsServerTool : VoxelForgeMcpServerTool
{
    public ClearReferenceModelsServerTool(ClearReferenceModelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class VoxelizeReferenceModelMcpTool : TypedConsoleCommandMcpTool
{
    public VoxelizeReferenceModelMcpTool(VoxelizeCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "voxelize_reference_model",
            "Convert a loaded reference model into voxels at a chosen resolution and mode (solid or surface).",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "minimum": 0, "description": "Reference model index." },
                    "resolution": { "type": "integer", "minimum": 2, "maximum": 256, "description": "Voxel grid resolution." },
                    "mode": { "type": "string", "enum": ["solid", "surface"], "description": "Voxelization mode. Defaults to solid." }
                },
                "required": ["index", "resolution"]
            }
            """),
            isReadOnly: false)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        if (!TryReadInt(arguments, "index", out int index, out errorMessage) ||
            !TryReadInt(arguments, "resolution", out int resolution, out errorMessage))
        {
            return false;
        }

        if (resolution < 2 || resolution > 256)
        {
            errorMessage = "Resolution must be between 2 and 256.";
            return false;
        }

        var args = new List<string> { FormatInt(index), FormatInt(resolution) };

        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String)
        {
            var mode = modeElement.GetString();
            if (mode is not null && !mode.Equals("solid", StringComparison.OrdinalIgnoreCase) && !mode.Equals("surface", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Mode must be 'solid' or 'surface'.";
                return false;
            }

            if (mode is not null)
                args.Add(mode);
        }

        commandArgs = args.ToArray();
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class VoxelizeReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public VoxelizeReferenceModelServerTool(VoxelizeReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}
