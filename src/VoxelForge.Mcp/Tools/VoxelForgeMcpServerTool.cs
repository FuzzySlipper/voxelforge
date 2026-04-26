using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace VoxelForge.Mcp.Tools;

public abstract class VoxelForgeMcpServerTool : McpServerTool
{
    private readonly IVoxelForgeMcpTool _tool;
    private readonly Tool _protocolTool;

    protected VoxelForgeMcpServerTool(IVoxelForgeMcpTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tool = tool;
        _protocolTool = new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema,
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = tool.IsReadOnly,
                OpenWorldHint = false,
            },
        };
    }

    public override Tool ProtocolTool => _protocolTool;

    public override IReadOnlyList<object> Metadata => [];

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var arguments = request.Params?.Arguments is null
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object?>())
            : JsonSerializer.SerializeToElement(request.Params.Arguments);

        var result = _tool.Invoke(arguments, cancellationToken);
        return ValueTask.FromResult(new CallToolResult
        {
            IsError = !result.Success,
            Content =
            [
                new TextContentBlock
                {
                    Text = result.Message,
                },
            ],
        });
    }
}
