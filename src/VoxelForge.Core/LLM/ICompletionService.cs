namespace VoxelForge.Core.LLM;

/// <summary>
/// Abstraction over LLM providers. Core never touches Anthropic/OpenAI SDK types.
/// </summary>
public interface ICompletionService
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default);
}
