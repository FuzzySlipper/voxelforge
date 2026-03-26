using System.Text.Json;

namespace VoxelForge.Core.LLM;

/// <summary>
/// LLM request/response types. No SDK dependencies — pure POCOs.
/// </summary>
public sealed class CompletionRequest
{
    public required string SystemPrompt { get; init; }
    public required List<CompletionMessage> Messages { get; init; }
    public List<ToolDefinition> Tools { get; init; } = [];
    public int MaxTokens { get; init; } = 4096;
}

public sealed class CompletionMessage
{
    public required string Role { get; init; }
    public string? TextContent { get; init; }
    public List<ToolCall>? ToolCalls { get; init; }
    public List<ToolResultContent>? ToolResults { get; init; }
}

public sealed class CompletionResponse
{
    public string? TextContent { get; init; }
    public List<ToolCall> ToolCalls { get; init; } = [];
    public string StopReason { get; init; } = "end_turn";
}

public sealed class ToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required JsonElement Arguments { get; init; }
}

public sealed class ToolResultContent
{
    public required string ToolCallId { get; init; }
    public required string Content { get; init; }
    public bool IsError { get; init; }
}

public sealed class ToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonElement ParametersSchema { get; init; }
}
