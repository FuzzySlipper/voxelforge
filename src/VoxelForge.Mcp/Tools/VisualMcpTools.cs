using System.Text.Json;

namespace VoxelForge.Mcp.Tools;

public abstract class UnavailableVisualMcpTool : IVoxelForgeMcpTool
{
    private readonly JsonElement _inputSchema;

    protected UnavailableVisualMcpTool(string name, string description, JsonElement inputSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Name = name;
        Description = description;
        _inputSchema = inputSchema;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema => _inputSchema;

    public bool IsReadOnly => true;

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new McpToolInvocationResult
        {
            Success = false,
            Message = $"Visual MCP tool '{Name}' is unavailable in headless VoxelForge.Mcp because screenshot capture requires the FNA renderer. Run the Engine.MonoGame application for viewport screenshots, or use non-visual query tools such as describe_model, get_model_info, and get_voxels_in_area.",
        };
    }
}

public sealed class ViewModelMcpTool : UnavailableVisualMcpTool
{
    public ViewModelMcpTool()
        : base(
            "view_model",
            "Capture a screenshot of the current voxel model viewport. Unavailable in headless MCP; returns a clear limitation error.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""))
    {
    }
}

public sealed class ViewModelServerTool : VoxelForgeMcpServerTool
{
    public ViewModelServerTool(ViewModelMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ViewFromAngleMcpTool : UnavailableVisualMcpTool
{
    public ViewFromAngleMcpTool()
        : base(
            "view_from_angle",
            "Capture a screenshot from a specific camera angle. Unavailable in headless MCP; returns a clear limitation error.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "yaw": { "type": "number", "description": "Camera yaw in radians (0=front, pi/2=right, pi=back)" },
                    "pitch": { "type": "number", "description": "Camera pitch in radians (0=level, pi/2=top-down)" }
                },
                "required": ["yaw", "pitch"]
            }
            """))
    {
    }
}

public sealed class ViewFromAngleServerTool : VoxelForgeMcpServerTool
{
    public ViewFromAngleServerTool(ViewFromAngleMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class CompareReferenceMcpTool : UnavailableVisualMcpTool
{
    public CompareReferenceMcpTool()
        : base(
            "compare_reference",
            "Capture the voxel model from 5 standard angles for visual comparison. Unavailable in headless MCP; returns a clear limitation error.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""))
    {
    }
}

public sealed class CompareReferenceServerTool : VoxelForgeMcpServerTool
{
    public CompareReferenceServerTool(CompareReferenceMcpTool tool)
        : base(tool)
    {
    }
}
