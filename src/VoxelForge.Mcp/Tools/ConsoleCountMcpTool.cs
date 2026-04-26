using VoxelForge.App.Console.Commands;

namespace VoxelForge.Mcp.Tools;

public sealed class ConsoleCountMcpTool : ConsoleCommandMcpTool
{
    public ConsoleCountMcpTool(CountCommand command, VoxelForgeMcpSession session)
        : base(command, session)
    {
    }
}

public sealed class ConsoleCountServerTool : VoxelForgeMcpServerTool
{
    public ConsoleCountServerTool(ConsoleCountMcpTool tool)
        : base(tool)
    {
    }
}
