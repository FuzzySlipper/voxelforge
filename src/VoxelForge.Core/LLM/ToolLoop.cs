using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.LLM;

/// <summary>
/// Single reusable call-LLM → check-tools → dispatch → repeat engine.
/// All agents share this one implementation.
/// </summary>
public sealed class ToolLoop
{
    private readonly ICompletionService _completion;
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;
    private readonly ILogger<ToolLoop> _logger;

    public ToolLoop(
        ICompletionService completion,
        IEnumerable<IToolHandler> handlers,
        ILogger<ToolLoop> logger)
    {
        _completion = completion;
        _handlers = handlers.ToDictionary(h => h.ToolName);
        _logger = logger;
    }

    public async Task<ToolLoopResult> RunAsync(
        string systemPrompt,
        string userMessage,
        VoxelModel model,
        LabelIndex labels,
        List<AnimationClip> clips,
        int maxRounds = 10,
        CancellationToken ct = default)
    {
        var tools = _handlers.Values.Select(h => h.GetDefinition()).ToList();
        var messages = new List<CompletionMessage>
        {
            new() { Role = "user", TextContent = userMessage },
        };

        var applyActions = new List<Action>();
        string? finalText = null;

        for (int round = 0; round < maxRounds; round++)
        {
            _logger.LogDebug("ToolLoop round {Round}", round + 1);

            var request = new CompletionRequest
            {
                SystemPrompt = systemPrompt,
                Messages = messages,
                Tools = tools,
            };

            var response = await _completion.CompleteAsync(request, ct);

            if (response.ToolCalls.Count == 0)
            {
                finalText = response.TextContent;
                _logger.LogDebug("ToolLoop finished: no tool calls, stop reason = {StopReason}", response.StopReason);
                break;
            }

            // Add assistant message with tool calls
            messages.Add(new CompletionMessage
            {
                Role = "assistant",
                TextContent = response.TextContent,
                ToolCalls = response.ToolCalls,
            });

            // Dispatch each tool call
            var toolResults = new List<ToolResultContent>();
            foreach (var call in response.ToolCalls)
            {
                if (!_handlers.TryGetValue(call.Name, out var handler))
                {
                    _logger.LogWarning("Unknown tool: {ToolName}", call.Name);
                    toolResults.Add(new ToolResultContent
                    {
                        ToolCallId = call.Id,
                        Content = $"Error: unknown tool '{call.Name}'",
                        IsError = true,
                    });
                    continue;
                }

                try
                {
                    var result = handler.Handle(call.Arguments, model, labels, clips);
                    if (result.ApplyAction is not null)
                        applyActions.Add(result.ApplyAction);

                    toolResults.Add(new ToolResultContent
                    {
                        ToolCallId = call.Id,
                        Content = result.Content,
                        IsError = result.IsError,
                    });
                    _logger.LogDebug("Tool {ToolName} returned: {Content}", call.Name,
                        result.Content.Length > 100 ? result.Content[..100] + "..." : result.Content);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tool {ToolName} threw an exception", call.Name);
                    toolResults.Add(new ToolResultContent
                    {
                        ToolCallId = call.Id,
                        Content = $"Error: {ex.Message}",
                        IsError = true,
                    });
                }
            }

            // Add tool results as a message
            messages.Add(new CompletionMessage
            {
                Role = "tool",
                ToolResults = toolResults,
            });
        }

        return new ToolLoopResult
        {
            ResponseText = finalText,
            ApplyActions = applyActions,
        };
    }
}

public sealed class ToolLoopResult
{
    public string? ResponseText { get; init; }
    public List<Action> ApplyActions { get; init; } = [];
}
