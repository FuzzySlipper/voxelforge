using Microsoft.Extensions.Logging;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Reference;
using VoxelForge.Content;
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

        var commands = new List<IConsoleCommand>
        {
            new DescribeCommand(),
            new SetVoxelConsoleCommand(),
            new RemoveVoxelConsoleCommand(),
            new FillCommand(),
            new GetVoxelCommand(),
            new GetCubeCommand(),
            new GetSphereCommand(),
            new CountCommand(),
            new UndoCommand(),
            new RedoCommand(),
            new ListRegionsCommand(),
            new LabelVoxelCommand(),
            new PaletteCommand(),
            new PaletteMapConsoleCommand(),
            new PaletteReduceConsoleCommand(),
            new AoBakeConsoleCommand(),
            new EdgeDarkenConsoleCommand(),
            new LightBakeConsoleCommand(),
            new SaveCommand(loggerFactory),
            new LoadCommand(loggerFactory),
            new ListFilesCommand(),
            new ClearCommand(),
            new GridCommand(),
            new ConfigCommand(config),
            new MeasureCommand(config),
            new RefLoadCommand(referenceModelState, refLoader),
            new RefListCommand(referenceModelState),
            new RefRemoveCommand(referenceModelState),
            new RefClearCommand(referenceModelState),
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
            new ImgLoadCommand(referenceImageState),
            new ImgListCommand(referenceImageState),
            new ImgRemoveCommand(referenceImageState),
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
