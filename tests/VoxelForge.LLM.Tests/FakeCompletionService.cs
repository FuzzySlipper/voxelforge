using VoxelForge.Core.LLM;

namespace VoxelForge.LLM.Tests;

/// <summary>
/// Scriptable fake for testing ToolLoop without a real LLM.
/// </summary>
public sealed class FakeCompletionService : ICompletionService
{
    private readonly Queue<CompletionResponse> _responses = new();
    public List<CompletionRequest> ReceivedRequests { get; } = [];

    public void EnqueueResponse(CompletionResponse response) => _responses.Enqueue(response);

    public void EnqueueTextResponse(string text)
    {
        _responses.Enqueue(new CompletionResponse { TextContent = text });
    }

    public void EnqueueToolCall(string toolName, string toolId, string argumentsJson)
    {
        _responses.Enqueue(new CompletionResponse
        {
            ToolCalls = [new ToolCall
            {
                Id = toolId,
                Name = toolName,
                Arguments = System.Text.Json.JsonDocument.Parse(argumentsJson).RootElement,
            }],
            StopReason = "tool_use",
        });
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);
        if (_responses.Count == 0)
            throw new InvalidOperationException("No more scripted responses in FakeCompletionService.");
        return Task.FromResult(_responses.Dequeue());
    }
}
