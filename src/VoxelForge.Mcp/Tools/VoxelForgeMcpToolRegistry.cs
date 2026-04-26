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
        services.AddSingleton<RegionEditingService>();
        services.AddSingleton<PaletteMaterialService>();
        services.AddSingleton<ProjectLifecycleService>();
        services.AddSingleton<ModelPathResolver>();
        services.AddSingleton<LlmToolApplicationService>();

        services.AddSingleton<DescribeModelHandler>();
        services.AddSingleton<GetModelInfoHandler>();
        services.AddSingleton<SetVoxelsHandler>();
        services.AddSingleton<RemoveVoxelsHandler>();
        services.AddSingleton<GetVoxelsInAreaHandler>();

        services.AddSingleton<DescribeModelMcpTool>();
        services.AddSingleton<GetModelInfoMcpTool>();
        services.AddSingleton<SetVoxelsMcpTool>();
        services.AddSingleton<RemoveVoxelsMcpTool>();
        services.AddSingleton<GetVoxelsInAreaMcpTool>();
        services.AddSingleton<ViewModelMcpTool>();
        services.AddSingleton<ViewFromAngleMcpTool>();
        services.AddSingleton<CompareReferenceMcpTool>();

        services.AddSingleton<McpServerTool, DescribeModelServerTool>();
        services.AddSingleton<McpServerTool, GetModelInfoServerTool>();
        services.AddSingleton<McpServerTool, SetVoxelsServerTool>();
        services.AddSingleton<McpServerTool, RemoveVoxelsServerTool>();
        services.AddSingleton<McpServerTool, GetVoxelsInAreaServerTool>();
        services.AddSingleton<McpServerTool, ViewModelServerTool>();
        services.AddSingleton<McpServerTool, ViewFromAngleServerTool>();
        services.AddSingleton<McpServerTool, CompareReferenceServerTool>();

        services.AddSingleton<FillCommand>();
        services.AddSingleton<GetVoxelCommand>();
        services.AddSingleton<CountCommand>();
        services.AddSingleton<ClearCommand>();
        services.AddSingleton<UndoCommand>();
        services.AddSingleton<RedoCommand>();

        services.AddSingleton<ConsoleCountMcpTool>();
        services.AddSingleton<FillBoxMcpTool>();
        services.AddSingleton<GetVoxelMcpTool>();
        services.AddSingleton<CountVoxelsMcpTool>();
        services.AddSingleton<ClearModelMcpTool>();
        services.AddSingleton<UndoMcpTool>();
        services.AddSingleton<RedoMcpTool>();

        services.AddSingleton<ListRegionsMcpTool>();
        services.AddSingleton<CreateRegionMcpTool>();
        services.AddSingleton<DeleteRegionMcpTool>();
        services.AddSingleton<AssignVoxelsToRegionMcpTool>();
        services.AddSingleton<GetRegionVoxelsMcpTool>();
        services.AddSingleton<GetRegionBoundsMcpTool>();
        services.AddSingleton<GetRegionTreeMcpTool>();

        services.AddSingleton<NewModelMcpTool>();
        services.AddSingleton<LoadModelMcpTool>();
        services.AddSingleton<SaveModelMcpTool>();
        services.AddSingleton<ListModelsMcpTool>();
        services.AddSingleton<ListPaletteMcpTool>();
        services.AddSingleton<SetPaletteEntryMcpTool>();
        services.AddSingleton<SetGridHintMcpTool>();

        services.AddSingleton<McpServerTool, ConsoleCountServerTool>();
        services.AddSingleton<McpServerTool, FillBoxServerTool>();
        services.AddSingleton<McpServerTool, GetVoxelServerTool>();
        services.AddSingleton<McpServerTool, CountVoxelsServerTool>();
        services.AddSingleton<McpServerTool, ClearModelServerTool>();
        services.AddSingleton<McpServerTool, UndoServerTool>();
        services.AddSingleton<McpServerTool, RedoServerTool>();

        services.AddSingleton<McpServerTool, ListRegionsServerTool>();
        services.AddSingleton<McpServerTool, CreateRegionServerTool>();
        services.AddSingleton<McpServerTool, DeleteRegionServerTool>();
        services.AddSingleton<McpServerTool, AssignVoxelsToRegionServerTool>();
        services.AddSingleton<McpServerTool, GetRegionVoxelsServerTool>();
        services.AddSingleton<McpServerTool, GetRegionBoundsServerTool>();
        services.AddSingleton<McpServerTool, GetRegionTreeServerTool>();

        services.AddSingleton<McpServerTool, NewModelServerTool>();
        services.AddSingleton<McpServerTool, LoadModelServerTool>();
        services.AddSingleton<McpServerTool, SaveModelServerTool>();
        services.AddSingleton<McpServerTool, ListModelsServerTool>();
        services.AddSingleton<McpServerTool, ListPaletteServerTool>();
        services.AddSingleton<McpServerTool, SetPaletteEntryServerTool>();
        services.AddSingleton<McpServerTool, SetGridHintServerTool>();

        return services;
    }
}
