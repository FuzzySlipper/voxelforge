using VoxelForge.App.Services;
using VoxelForge.Core.LLM.Handlers;

namespace VoxelForge.Mcp.Tools;

public sealed class DescribeModelMcpTool : LlmToolMcpTool
{
    public DescribeModelMcpTool(
        DescribeModelHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService)
    {
    }
}

public sealed class DescribeModelServerTool : VoxelForgeMcpServerTool
{
    public DescribeModelServerTool(DescribeModelMcpTool tool)
        : base(tool)
    {
    }
}
