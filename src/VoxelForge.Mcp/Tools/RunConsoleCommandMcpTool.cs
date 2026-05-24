using System.Text.Json;
using VoxelForge.App.Console;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP tool that routes a named console command through CommandRouter/IConsoleCommand
/// with tokenized arguments and a mutation guard.
///
/// This is a dev/manual fallback for commands not yet exposed as first-class typed MCP tools.
/// Stable, high-frequency workflows should be promoted to typed MCP tools instead.
/// </summary>
public sealed class RunConsoleCommandMcpTool : IVoxelForgeMcpTool
{
    private readonly ConsoleCommandBridgeService _bridge;
    private readonly VoxelForgeMcpSession _session;
    private static readonly JsonElement _inputSchema;

    public string Name => "run_console_command";

    public string Description =>
        "Execute a headless console command by name with tokenized arguments. " +
        "This is a dev/manual fallback for commands not yet promoted to typed MCP tools. " +
        "Regular workflows should use the stable typed MCP tool instead. " +
        "Mutating commands require allow_mutation=true. Args are passed as an already-tokenized array; " +
        "no command-line string is reconstructed.";

    public bool IsReadOnly => false; // Depends on the command; guarded at invocation.

    public JsonElement InputSchema => _inputSchema;

    static RunConsoleCommandMcpTool()
    {
        using var doc = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "command": {
                    "type": "string",
                    "description": "Console command name (or alias) to execute."
                },
                "args": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Already-tokenized command arguments — never reconstructed into a shell command string."
                },
                "allow_mutation": {
                    "type": "boolean",
                    "description": "Explicit opt-in for mutating commands. Required when the command modifies model state.",
                    "default": false
                }
            },
            "required": ["command"]
        }
        """);
        _inputSchema = doc.RootElement.Clone();
    }

    public RunConsoleCommandMcpTool(ConsoleCommandBridgeService bridge, VoxelForgeMcpSession session)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(session);
        _bridge = bridge;
        _session = session;
    }

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Parse inputs
        string? commandName = null;
        string[] args = [];
        bool allowMutation = false;

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("command", out var cmdEl) && cmdEl.ValueKind == JsonValueKind.String)
                commandName = cmdEl.GetString();

            if (arguments.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<string>();
                foreach (var item in argsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        parsed.Add(item.GetString() ?? string.Empty);
                }
                args = [.. parsed];
            }

            if (arguments.TryGetProperty("allow_mutation", out var mutEl) && mutEl.ValueKind == JsonValueKind.True)
                allowMutation = true;
        }

        if (string.IsNullOrWhiteSpace(commandName))
        {
            return new McpToolInvocationResult
            {
                Success = false,
                Message = "Missing required field 'command'.",
            };
        }

        // Execute via bridge (catalog lookup + mutation guard + CommandRouter).
        BridgeExecutionResult bridgeResult;
        lock (_session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bridgeResult = _bridge.Execute(commandName, args, allowMutation, _session.CommandContext);
        }

        // Serialize result as JSON with metadata.
        var resultObj = new
        {
            success = bridgeResult.Success,
            command = bridgeResult.Command,
            args = bridgeResult.Args,
            message = bridgeResult.Message,
            mutates_state = bridgeResult.MutatesState,
            allow_mutation = bridgeResult.AllowMutation,
        };

        var resultJson = JsonSerializer.Serialize(resultObj, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

        return new McpToolInvocationResult
        {
            Success = bridgeResult.Success,
            Message = resultJson,
        };
    }
}
