using System.Text.Json;

namespace VoxelForge.Core.LLM;

/// <summary>
/// Handles a single LLM tool call. Each handler is a named type — no lambdas.
/// </summary>
public interface IToolHandler
{
    string ToolName { get; }
    ToolDefinition GetDefinition();
    ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips);
}

/// <summary>
/// Result of a tool handler invocation: content for the LLM and an action to apply.
/// </summary>
public sealed class ToolHandlerResult
{
    public required string Content { get; init; }
    public bool IsError { get; init; }
    /// <summary>
    /// Optional action that modifies the model. Called by ToolLoop after generating the result.
    /// Null if the tool is read-only (query/describe).
    /// </summary>
    public Action? ApplyAction { get; init; }
}
