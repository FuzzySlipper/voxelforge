using System.Text.Json;
using VoxelForge.App.Services;
using VoxelForge.Core.LLM;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP adapter for a VoxelForge LLM tool handler. It parses transport input,
/// delegates to the typed handler/service path, and formats the result for MCP.
/// </summary>
public abstract class LlmToolMcpTool : IVoxelForgeMcpTool
{
    private readonly IToolHandler _handler;
    private readonly VoxelForgeMcpSession _session;
    private readonly LlmToolApplicationService _applicationService;
    private readonly ToolDefinition _definition;

    protected LlmToolMcpTool(
        IToolHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(applicationService);

        _handler = handler;
        _session = session;
        _applicationService = applicationService;
        _definition = handler.GetDefinition();
    }

    public string Name => _definition.Name;

    public string Description => _definition.Description;

    public JsonElement InputSchema => _definition.ParametersSchema;

    public McpToolInvocationResult Invoke(JsonElement arguments)
    {
        ToolHandlerResult handlerResult;
        lock (_session.SyncRoot)
        {
            handlerResult = _handler.Handle(
                arguments,
                _session.Document.Model,
                _session.Document.Labels,
                _session.Document.Clips);

            if (handlerResult.MutationIntent is not null && !handlerResult.IsError)
            {
                var applicationResult = _applicationService.ApplyMutationIntents(
                    _session.Document,
                    _session.UndoStack,
                    _session.Events,
                    new ApplyLlmMutationIntentsRequest([handlerResult.MutationIntent]));

                return new McpToolInvocationResult
                {
                    Success = applicationResult.Success,
                    Message = applicationResult.Success
                        ? handlerResult.Content + "\n" + applicationResult.Message
                        : applicationResult.Message,
                };
            }
        }

        return new McpToolInvocationResult
        {
            Success = !handlerResult.IsError,
            Message = handlerResult.Content,
        };
    }
}
