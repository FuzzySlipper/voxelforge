using System.Text.Json;
using VoxelForge.App.Console;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Compatibility adapter for console commands that have not yet been promoted to
/// first-class MCP request DTOs. It calls the named command directly with an
/// argument array; it never rebuilds a command line string.
/// </summary>
public abstract class ConsoleCommandMcpTool : IVoxelForgeMcpTool
{
    private readonly IConsoleCommand _command;
    private readonly VoxelForgeMcpSession _session;
    private readonly JsonElement _inputSchema;

    protected ConsoleCommandMcpTool(IConsoleCommand command, VoxelForgeMcpSession session)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);

        _command = command;
        _session = session;
        _inputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "args": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Command arguments as already-tokenized strings."
                }
            }
        }
        """).RootElement.Clone();
    }

    public string Name => "console_" + _command.Name;

    public string Description => "Run the headless console command directly: " + _command.HelpText;

    public JsonElement InputSchema => _inputSchema;

    public McpToolInvocationResult Invoke(JsonElement arguments)
    {
        string[] commandArgs = [];
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("args", out var argsElement) &&
            argsElement.ValueKind == JsonValueKind.Array)
        {
            var parsedArgs = new List<string>();
            foreach (var item in argsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    parsedArgs.Add(item.GetString() ?? string.Empty);
            }

            commandArgs = [.. parsedArgs];
        }

        CommandResult commandResult;
        lock (_session.SyncRoot)
            commandResult = _command.Execute(commandArgs, _session.CommandContext);

        return new McpToolInvocationResult
        {
            Success = commandResult.Success,
            Message = commandResult.Message,
        };
    }
}
