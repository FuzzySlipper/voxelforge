using System.Text.Json;
using VoxelForge.App.Console;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP tool that lists all bridge-accessible console commands with their metadata:
/// names, aliases, help text, mutates-state classification, and allow_mutation requirement.
/// </summary>
public sealed class ListConsoleCommandsMcpTool : IVoxelForgeMcpTool
{
    private static readonly JsonElement _inputSchema;
    private static readonly JsonElement _responseExample;

    public string Name => "list_console_commands";

    public string Description =>
        "List all console commands available through the guarded manual console bridge, " +
        "with names, aliases, help/usage text, mutation classification, and allow_mutation requirements. " +
        "Only commands in the explicit bridge catalog are listed; unknown or denied commands are rejected. " +
        "Regular workflows should use stable typed MCP tools instead of this bridge.";

    public bool IsReadOnly => true;

    public JsonElement InputSchema => _inputSchema;

    static ListConsoleCommandsMcpTool()
    {
        using var doc = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        _inputSchema = doc.RootElement.Clone();

        using var example = JsonDocument.Parse("""
        [
            {
                "name": "count",
                "aliases": [],
                "help_text": "Count voxels, optionally filtered. Usage: count | count <paletteIndex> | count cube <x1> <y1> <z1> <x2> <y2> <z2>",
                "mutates_state": false,
                "requires_allow_mutation": false,
                "bridge_notes": null
            },
            {
                "name": "fill",
                "aliases": ["f"],
                "help_text": "Fill a region. Usage: fill <x1> <y1> <z1> <x2> <y2> <z2> <paletteIndex>",
                "mutates_state": true,
                "requires_allow_mutation": true,
                "bridge_notes": "Mutates model state — requires allow_mutation: true."
            }
        ]
        """);
        _responseExample = example.RootElement.Clone();
    }

    private readonly ConsoleCommandBridgeService _bridge;

    public ListConsoleCommandsMcpTool(ConsoleCommandBridgeService bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
    }

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = _bridge.UniqueEntries
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new
            {
                name = e.Name,
                aliases = e.Aliases,
                help_text = e.HelpText,
                mutates_state = e.MutatesState,
                requires_allow_mutation = e.RequiresAllowMutation,
                bridge_notes = e.BridgeNotes,
            })
            .ToArray();

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        return new McpToolInvocationResult
        {
            Success = true,
            Message = json,
        };
    }
}
