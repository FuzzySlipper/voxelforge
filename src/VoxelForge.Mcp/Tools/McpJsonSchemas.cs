using System.Text.Json;

namespace VoxelForge.Mcp.Tools;

internal static class McpJsonSchemas
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
