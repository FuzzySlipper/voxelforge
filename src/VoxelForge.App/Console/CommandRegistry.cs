using Microsoft.Extensions.Logging;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core.Services;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.App.Console;

/// <summary>
/// Builds the full set of console commands. Explicit registration — no reflection.
/// </summary>
public static class CommandRegistry
{
    public static CommandRouter Build(
        ILoggerFactory loggerFactory,
        EditorConfigState config,
        ReferenceModelState referenceModelState,
        ReferenceModelLoader refLoader,
        ReferenceImageState referenceImageState,
        Func<IScreenshotProvider?>? screenshotFactory = null)
    {
        var logger = loggerFactory.CreateLogger<CommandRouter>();
        var voxelEditingService = new VoxelEditingService();
        var voxelQueryService = new VoxelQueryService();
        var regionEditingService = new RegionEditingService();
        var paletteMaterialService = new PaletteMaterialService();
        var projectLifecycleService = new ProjectLifecycleService(loggerFactory);
        var referenceAssetService = new ReferenceAssetService(refLoader);

        var commands = new List<IConsoleCommand>
        {
            new DescribeCommand(voxelQueryService),
            new SetVoxelConsoleCommand(voxelEditingService),
            new RemoveVoxelConsoleCommand(voxelEditingService),
            new FillCommand(voxelEditingService),
            new GetVoxelCommand(voxelQueryService),
            new GetCubeCommand(voxelQueryService),
            new GetSphereCommand(voxelQueryService),
            new CountCommand(voxelQueryService),
            new UndoCommand(),
            new RedoCommand(),
            new ListRegionsCommand(regionEditingService),
            new LabelVoxelCommand(regionEditingService),
            new PaletteCommand(paletteMaterialService),
            new PaletteMapConsoleCommand(),
            new PaletteReduceConsoleCommand(),
            new AoBakeConsoleCommand(),
            new EdgeDarkenConsoleCommand(),
            new LightBakeConsoleCommand(),
            new SaveCommand(projectLifecycleService),
            new LoadCommand(projectLifecycleService),
            new ListFilesCommand(),
            new ClearCommand(voxelEditingService),
            new GridCommand(voxelEditingService),
            new ConfigCommand(config),
            new MeasureCommand(config),
            new RefLoadCommand(referenceModelState, referenceAssetService),
            new RefListCommand(referenceModelState, referenceAssetService),
            new RefRemoveCommand(referenceModelState, referenceAssetService),
            new RefClearCommand(referenceModelState, referenceAssetService),
            new RefTransformCommand(referenceModelState),
            new RefModeCommand(referenceModelState),
            new RefVisibilityCommand(referenceModelState, show: true),
            new RefVisibilityCommand(referenceModelState, show: false),
            new RefScaleCommand(referenceModelState),
            new RefRotateCommand(referenceModelState),
            new RefOrientCommand(referenceModelState),
            new RefInfoCommand(referenceModelState, refLoader),
            new RefAnimCommand(referenceModelState),
            new RefTexCommand(referenceModelState, refLoader),
            new RefTexEmissiveCommand(referenceModelState, refLoader),
            new RefSaveMetaCommand(referenceModelState),
            new RefLoadMetaCommand(referenceModelState, refLoader),
            new ImgLoadCommand(referenceImageState, referenceAssetService),
            new ImgListCommand(referenceImageState, referenceAssetService),
            new ImgRemoveCommand(referenceImageState, referenceAssetService),
            new ScreenshotCommand(screenshotFactory ?? (() => null)),
            new VoxelizeCommand(referenceModelState, loggerFactory),
            new VoxelizeCompareCommand(referenceModelState, loggerFactory),
        };

        var router = new CommandRouter(commands, logger);
        var allCommands = new List<IConsoleCommand>(commands)
        {
            new HelpCommand(router),
            new ExecCommand(router),
        };
        router = new CommandRouter(allCommands, logger);

        return router;
    }
}
