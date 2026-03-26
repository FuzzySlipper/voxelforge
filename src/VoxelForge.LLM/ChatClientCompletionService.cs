using System.Text.Json;
using Microsoft.Extensions.AI;
using VoxelForge.Core.LLM;

namespace VoxelForge.LLM;

/// <summary>
/// Adapts Microsoft.Extensions.AI.IChatClient to VoxelForge's ICompletionService.
/// This is the only place SDK types are referenced.
/// </summary>
public sealed class ChatClientCompletionService : ICompletionService
{
    private readonly IChatClient _client;

    public ChatClientCompletionService(IChatClient client)
    {
        _client = client;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>();

        // System prompt
        var systemMsg = new ChatMessage();
        systemMsg.Role = ChatRole.System;
        systemMsg.Contents.Add(new TextContent(request.SystemPrompt));
        messages.Add(systemMsg);

        foreach (var msg in request.Messages)
        {
            var chatMsg = new ChatMessage();

            switch (msg.Role)
            {
                case "user":
                    chatMsg.Role = ChatRole.User;
                    if (msg.TextContent is not null)
                        chatMsg.Contents.Add(new TextContent(msg.TextContent));
                    break;

                case "assistant":
                    chatMsg.Role = ChatRole.Assistant;
                    if (msg.TextContent is not null)
                        chatMsg.Contents.Add(new TextContent(msg.TextContent));
                    if (msg.ToolCalls is not null)
                    {
                        foreach (var tc in msg.ToolCalls)
                            chatMsg.Contents.Add(new FunctionCallContent(tc.Id, tc.Name,
                                JsonSerializer.Deserialize<Dictionary<string, object?>>(tc.Arguments.GetRawText())));
                    }
                    break;

                case "tool":
                    chatMsg.Role = ChatRole.Tool;
                    if (msg.ToolResults is not null)
                    {
                        foreach (var tr in msg.ToolResults)
                            chatMsg.Contents.Add(new FunctionResultContent(tr.ToolCallId, tr.Content));
                    }
                    break;
            }

            messages.Add(chatMsg);
        }

        var options = new ChatOptions
        {
            MaxOutputTokens = request.MaxTokens,
        };

        var result = await _client.GetResponseAsync(messages, options, ct);

        // Extract text and tool calls from response
        string? text = null;
        var toolCalls = new List<Core.LLM.ToolCall>();

        foreach (var content in result.Messages.SelectMany(m => m.Contents))
        {
            if (content is TextContent tc)
                text = (text ?? "") + tc.Text;
            else if (content is FunctionCallContent fcc)
            {
                toolCalls.Add(new Core.LLM.ToolCall
                {
                    Id = fcc.CallId ?? Guid.NewGuid().ToString(),
                    Name = fcc.Name,
                    Arguments = JsonSerializer.SerializeToElement(fcc.Arguments),
                });
            }
        }

        return new CompletionResponse
        {
            TextContent = text,
            ToolCalls = toolCalls,
            StopReason = result.FinishReason?.ToString() ?? "end_turn",
        };
    }
}
