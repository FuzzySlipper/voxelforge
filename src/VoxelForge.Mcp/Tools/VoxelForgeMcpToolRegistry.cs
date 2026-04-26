using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Explicit MCP tool registration. Do not replace with assembly scanning.
/// </summary>
public static class VoxelForgeMcpToolRegistry
{
    public static IServiceCollection AddVoxelForgeMcpTools(this IServiceCollection services)
    {
        services.AddSingleton<VoxelQueryService>();
        services.AddSingleton<VoxelMutationIntentService>();
        services.AddSingleton<VoxelEditingService>();
        services.AddSingleton<LlmToolApplicationService>();

        services.AddSingleton<DescribeModelHandler>();
        services.AddSingleton<DescribeModelMcpTool>();
        services.AddSingleton<McpServerTool, DescribeModelServerTool>();

        services.AddSingleton<CountCommand>();
        services.AddSingleton<ConsoleCountMcpTool>();
        services.AddSingleton<McpServerTool, ConsoleCountServerTool>();

        return services;
    }
}
