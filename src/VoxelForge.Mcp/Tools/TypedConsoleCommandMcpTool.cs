using System.Globalization;
using System.Text.Json;
using VoxelForge.App.Console;

namespace VoxelForge.Mcp.Tools;

public abstract class TypedConsoleCommandMcpTool : IVoxelForgeMcpTool
{
    private readonly IConsoleCommand _command;
    private readonly VoxelForgeMcpSession _session;
    private readonly JsonElement _inputSchema;

    protected TypedConsoleCommandMcpTool(
        IConsoleCommand command,
        VoxelForgeMcpSession session,
        string name,
        string description,
        JsonElement inputSchema,
        bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        _command = command;
        _session = session;
        Name = name;
        Description = description;
        _inputSchema = inputSchema;
        IsReadOnly = isReadOnly;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema => _inputSchema;

    public bool IsReadOnly { get; }

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryBuildCommandArguments(arguments, out var commandArgs, out var errorMessage))
        {
            return new McpToolInvocationResult
            {
                Success = false,
                Message = errorMessage,
            };
        }

        CommandResult commandResult;
        lock (_session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            commandResult = _command.Execute(commandArgs, _session.CommandContext);
        }

        return new McpToolInvocationResult
        {
            Success = commandResult.Success,
            Message = commandResult.Message,
        };
    }

    protected abstract bool TryBuildCommandArguments(
        JsonElement arguments,
        out string[] commandArgs,
        out string errorMessage);

    protected static bool TryReadInt(JsonElement arguments, string propertyName, out int value, out string errorMessage)
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
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Null)
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

    protected static string FormatInt(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
