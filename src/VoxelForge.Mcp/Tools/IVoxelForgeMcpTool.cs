using System.Text.Json;

namespace VoxelForge.Mcp.Tools;

public interface IVoxelForgeMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }
    bool IsReadOnly { get; }
    McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken);
}

public sealed class McpToolInvocationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
}
