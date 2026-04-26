using System.Text.Json;
using VoxelForge.App.Console.Commands;

namespace VoxelForge.Mcp.Tools;

public sealed class FillBoxMcpTool : TypedConsoleCommandMcpTool
{
    public FillBoxMcpTool(FillCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "fill_box",
            "Fill an inclusive cuboid region with a palette index using the headless fill console command.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "x1": { "type": "integer" },
                    "y1": { "type": "integer" },
                    "z1": { "type": "integer" },
                    "x2": { "type": "integer" },
                    "y2": { "type": "integer" },
                    "z2": { "type": "integer" },
                    "palette_index": { "type": "integer", "minimum": 1, "maximum": 255 }
                },
                "required": ["x1", "y1", "z1", "x2", "y2", "z2", "palette_index"]
            }
            """),
            isReadOnly: false)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        if (!TryReadInt(arguments, "x1", out int x1, out errorMessage) ||
            !TryReadInt(arguments, "y1", out int y1, out errorMessage) ||
            !TryReadInt(arguments, "z1", out int z1, out errorMessage) ||
            !TryReadInt(arguments, "x2", out int x2, out errorMessage) ||
            !TryReadInt(arguments, "y2", out int y2, out errorMessage) ||
            !TryReadInt(arguments, "z2", out int z2, out errorMessage) ||
            !TryReadInt(arguments, "palette_index", out int paletteIndex, out errorMessage))
        {
            return false;
        }

        commandArgs =
        [
            FormatInt(x1), FormatInt(y1), FormatInt(z1),
            FormatInt(x2), FormatInt(y2), FormatInt(z2),
            FormatInt(paletteIndex),
        ];
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class FillBoxServerTool : VoxelForgeMcpServerTool
{
    public FillBoxServerTool(FillBoxMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetVoxelMcpTool : TypedConsoleCommandMcpTool
{
    public GetVoxelMcpTool(GetVoxelCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "get_voxel",
            "Get the palette index and material name at a single voxel coordinate using the headless get console command.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "x": { "type": "integer" },
                    "y": { "type": "integer" },
                    "z": { "type": "integer" }
                },
                "required": ["x", "y", "z"]
            }
            """),
            isReadOnly: true)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        if (!TryReadInt(arguments, "x", out int x, out errorMessage) ||
            !TryReadInt(arguments, "y", out int y, out errorMessage) ||
            !TryReadInt(arguments, "z", out int z, out errorMessage))
        {
            return false;
        }

        commandArgs = [FormatInt(x), FormatInt(y), FormatInt(z)];
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class GetVoxelServerTool : VoxelForgeMcpServerTool
{
    public GetVoxelServerTool(GetVoxelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class CountVoxelsMcpTool : TypedConsoleCommandMcpTool
{
    public CountVoxelsMcpTool(CountCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "count_voxels",
            "Count voxels in the model, optionally filtered by palette index or inclusive cuboid box.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "palette_index": { "type": "integer", "minimum": 1, "maximum": 255 },
                    "box": {
                        "type": "object",
                        "properties": {
                            "x1": { "type": "integer" },
                            "y1": { "type": "integer" },
                            "z1": { "type": "integer" },
                            "x2": { "type": "integer" },
                            "y2": { "type": "integer" },
                            "z2": { "type": "integer" }
                        },
                        "required": ["x1", "y1", "z1", "x2", "y2", "z2"]
                    }
                }
            }
            """),
            isReadOnly: true)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        JsonElement boxElement = default;
        bool hasBox = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("box", out boxElement) && boxElement.ValueKind != JsonValueKind.Null;
        if (!TryReadOptionalInt(arguments, "palette_index", out int paletteIndex, out bool hasPaletteIndex, out errorMessage))
            return false;

        if (hasBox && hasPaletteIndex)
        {
            errorMessage = "Provide either 'box' or 'palette_index', not both.";
            return false;
        }

        if (hasBox)
        {
            if (boxElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Property 'box' must be an object when provided.";
                return false;
            }

            if (!TryReadInt(boxElement, "x1", out int x1, out errorMessage) ||
                !TryReadInt(boxElement, "y1", out int y1, out errorMessage) ||
                !TryReadInt(boxElement, "z1", out int z1, out errorMessage) ||
                !TryReadInt(boxElement, "x2", out int x2, out errorMessage) ||
                !TryReadInt(boxElement, "y2", out int y2, out errorMessage) ||
                !TryReadInt(boxElement, "z2", out int z2, out errorMessage))
            {
                return false;
            }

            commandArgs =
            [
                "cube",
                FormatInt(x1), FormatInt(y1), FormatInt(z1),
                FormatInt(x2), FormatInt(y2), FormatInt(z2),
            ];
            errorMessage = string.Empty;
            return true;
        }

        if (hasPaletteIndex)
            commandArgs = [FormatInt(paletteIndex)];

        errorMessage = string.Empty;
        return true;
    }
}

public sealed class CountVoxelsServerTool : VoxelForgeMcpServerTool
{
    public CountVoxelsServerTool(CountVoxelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ClearModelMcpTool : TypedConsoleCommandMcpTool
{
    public ClearModelMcpTool(ClearCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "clear_model",
            "Remove all voxels from the model using the headless clear console command.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: false)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class ClearModelServerTool : VoxelForgeMcpServerTool
{
    public ClearModelServerTool(ClearModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class UndoMcpTool : TypedConsoleCommandMcpTool
{
    public UndoMcpTool(UndoCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "undo",
            "Undo the last model editing operation in the MCP session.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: false)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class UndoServerTool : VoxelForgeMcpServerTool
{
    public UndoServerTool(UndoMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class RedoMcpTool : TypedConsoleCommandMcpTool
{
    public RedoMcpTool(RedoCommand command, VoxelForgeMcpSession session)
        : base(
            command,
            session,
            "redo",
            "Redo the last undone model editing operation in the MCP session.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: false)
    {
    }

    protected override bool TryBuildCommandArguments(JsonElement arguments, out string[] commandArgs, out string errorMessage)
    {
        commandArgs = [];
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class RedoServerTool : VoxelForgeMcpServerTool
{
    public RedoServerTool(RedoMcpTool tool)
        : base(tool)
    {
    }
}
