using ModelContextProtocol.Server;

namespace VoxelForge.Mcp.Tools;

public sealed class RunConsoleCommandServerTool : VoxelForgeMcpServerTool
{
    public RunConsoleCommandServerTool(RunConsoleCommandMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ListConsoleCommandsServerTool : VoxelForgeMcpServerTool
{
    public ListConsoleCommandsServerTool(ListConsoleCommandsMcpTool tool)
        : base(tool)
    {
    }
}
