using System.Text.Json;
using VoxelForge.Core.Services;

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
/// Result of a tool handler invocation: content for the LLM and an optional typed mutation intent.
/// </summary>
public sealed class ToolHandlerResult
{
    public required string Content { get; init; }
    public bool IsError { get; init; }
    public VoxelMutationIntent? MutationIntent { get; init; }
}
