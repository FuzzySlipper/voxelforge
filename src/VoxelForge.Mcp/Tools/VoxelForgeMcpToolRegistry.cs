using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using VoxelForge.App;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Services;

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
        services.AddSingleton<VoxelPrimitiveGenerationService>();
        services.AddSingleton<VoxelEditingService>();
        services.AddSingleton<RegionEditingService>();
        services.AddSingleton<PaletteMaterialService>();
        services.AddSingleton<ProjectLifecycleService>();
        services.AddSingleton<ModelPathResolver>();
        services.AddSingleton<SpatialQueryService>();
        services.AddSingleton<LlmToolApplicationService>();
        services.AddSingleton<ReferenceModelLoader>(sp => new ReferenceModelLoader(
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ReferenceModelLoader>()));
        services.AddSingleton<ReferenceAssetService>();
        services.AddSingleton<ReferenceImageState>();
        services.AddSingleton<VoxelizeCommand>(sp => new VoxelizeCommand(
            sp.GetRequiredService<VoxelForgeMcpSession>().ReferenceModels,
            sp.GetRequiredService<ILoggerFactory>()));

        // Build the full VoxelForge CommandRouter using shared DI singletons so the MCP bridge
        // catalog is derived from the same registry the interactive console uses.
        services.AddSingleton(sp => CommandRegistry.Build(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<VoxelEditingService>(),
            sp.GetRequiredService<VoxelQueryService>(),
            sp.GetRequiredService<RegionEditingService>(),
            sp.GetRequiredService<PaletteMaterialService>(),
            sp.GetRequiredService<ProjectLifecycleService>(),
            sp.GetRequiredService<ReferenceAssetService>(),
            sp.GetRequiredService<EditorConfigService>(),
            sp.GetRequiredService<EditorConfigState>(),
            sp.GetRequiredService<VoxelForgeMcpSession>().ReferenceModels,
            sp.GetRequiredService<ReferenceModelLoader>(),
            sp.GetRequiredService<ReferenceImageState>(),
            () => null));

        services.AddSingleton<DescribeModelHandler>();
        services.AddSingleton<GetModelInfoHandler>();
        services.AddSingleton<SetVoxelsHandler>();
        services.AddSingleton<RemoveVoxelsHandler>();
        services.AddSingleton<GetVoxelsInAreaHandler>();
        services.AddSingleton<ApplyVoxelPrimitivesHandler>();

        services.AddSingleton<SetVoxelsRunsHandler>();

        services.AddSingleton<DescribeModelMcpTool>();
        services.AddSingleton<GetModelInfoMcpTool>();
        services.AddSingleton<SetVoxelsMcpTool>();
        services.AddSingleton<RemoveVoxelsMcpTool>();
        services.AddSingleton<GetVoxelsInAreaMcpTool>();
        services.AddSingleton<ApplyVoxelPrimitivesMcpTool>();
        services.AddSingleton<SetVoxelsRunsMcpTool>();
        services.AddSingleton<ViewModelMcpTool>();
        services.AddSingleton<ViewFromAngleMcpTool>();
        services.AddSingleton<CaptureReferenceViewsMcpTool>();

        services.AddSingleton<McpServerTool, DescribeModelServerTool>();
        services.AddSingleton<McpServerTool, GetModelInfoServerTool>();
        services.AddSingleton<McpServerTool, SetVoxelsServerTool>();
        services.AddSingleton<McpServerTool, RemoveVoxelsServerTool>();
        services.AddSingleton<McpServerTool, GetVoxelsInAreaServerTool>();
        services.AddSingleton<McpServerTool, ApplyVoxelPrimitivesServerTool>();
        services.AddSingleton<McpServerTool, SetVoxelsRunsServerTool>();
        services.AddSingleton<McpServerTool, ViewModelServerTool>();
        services.AddSingleton<McpServerTool, ViewFromAngleServerTool>();
        services.AddSingleton<McpServerTool, CaptureReferenceViewsServerTool>();

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
        services.AddSingleton<PublishPreviewMcpTool>();
        services.AddSingleton<ListModelsMcpTool>();
        services.AddSingleton<ListPaletteMcpTool>();
        services.AddSingleton<SetPaletteEntryMcpTool>();
        services.AddSingleton<SetGridHintMcpTool>();

        services.AddSingleton<LoadReferenceModelMcpTool>();
        services.AddSingleton<ListReferenceModelsMcpTool>();
        services.AddSingleton<TransformReferenceModelMcpTool>();
        services.AddSingleton<RemoveReferenceModelMcpTool>();
        services.AddSingleton<ClearReferenceModelsMcpTool>();
        services.AddSingleton<VoxelizeReferenceModelMcpTool>();
        services.AddSingleton<GetReferenceModelDiagnosticsMcpTool>();
        services.AddSingleton<SuggestReferenceTransformMcpTool>();
        services.AddSingleton<FitReferenceModelMcpTool>();

        services.AddSingleton<RaycastReferenceModelMcpTool>();
        services.AddSingleton<SampleReferenceModelViewsMcpTool>();
        services.AddSingleton<ReferenceModelAxisHistogramMcpTool>();

        // Manual texture override tools
        services.AddSingleton<InspectReferenceMaterialsMcpTool>();
        services.AddSingleton<SetReferenceModelTextureMcpTool>();
        services.AddSingleton<SetReferenceTextureSamplingMcpTool>();

        services.AddSingleton<GetRegionNeighborsMcpTool>();
        services.AddSingleton<GetInterfaceVoxelsMcpTool>();
        services.AddSingleton<MeasureDistanceMcpTool>();
        services.AddSingleton<GetCrossSectionMcpTool>();
        services.AddSingleton<CheckCollisionMcpTool>();

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
        services.AddSingleton<McpServerTool, PublishPreviewServerTool>();
        services.AddSingleton<McpServerTool, ListModelsServerTool>();
        services.AddSingleton<McpServerTool, ListPaletteServerTool>();
        services.AddSingleton<McpServerTool, SetPaletteEntryServerTool>();
        services.AddSingleton<McpServerTool, SetGridHintServerTool>();

        services.AddSingleton<McpServerTool, LoadReferenceModelServerTool>();
        services.AddSingleton<McpServerTool, ListReferenceModelsServerTool>();
        services.AddSingleton<McpServerTool, TransformReferenceModelServerTool>();
        services.AddSingleton<McpServerTool, RemoveReferenceModelServerTool>();
        services.AddSingleton<McpServerTool, ClearReferenceModelsServerTool>();
        services.AddSingleton<McpServerTool, VoxelizeReferenceModelServerTool>();
        services.AddSingleton<McpServerTool, GetReferenceModelDiagnosticsServerTool>();
        services.AddSingleton<McpServerTool, SuggestReferenceTransformServerTool>();
        services.AddSingleton<McpServerTool, FitReferenceModelServerTool>();

        services.AddSingleton<McpServerTool, RaycastReferenceModelServerTool>();
        services.AddSingleton<McpServerTool, SampleReferenceModelViewsServerTool>();
        services.AddSingleton<McpServerTool, ReferenceModelAxisHistogramServerTool>();

        // Manual texture override server tools
        services.AddSingleton<McpServerTool, InspectReferenceMaterialsServerTool>();
        services.AddSingleton<McpServerTool, SetReferenceModelTextureServerTool>();
        services.AddSingleton<McpServerTool, SetReferenceTextureSamplingServerTool>();

        services.AddSingleton<McpServerTool, GetRegionNeighborsServerTool>();
        services.AddSingleton<McpServerTool, GetInterfaceVoxelsServerTool>();
        services.AddSingleton<McpServerTool, MeasureDistanceServerTool>();
        services.AddSingleton<McpServerTool, GetCrossSectionServerTool>();
        services.AddSingleton<McpServerTool, CheckCollisionServerTool>();

        // Console command bridge — guarded manual fallback for dev/low-frequency commands.
        // The bridge now derives its catalog from the shared CommandRouter so all VoxelForge
        // console commands are exposed automatically (with documented exclusions).
        services.AddSingleton<EditorConfigService>();
        services.AddSingleton<ConsoleCommandBridgeService>();
        services.AddSingleton<RunConsoleCommandMcpTool>();
        services.AddSingleton<ListConsoleCommandsMcpTool>();
        services.AddSingleton<McpServerTool, RunConsoleCommandServerTool>();
        services.AddSingleton<McpServerTool, ListConsoleCommandsServerTool>();

        return services;
    }
}
