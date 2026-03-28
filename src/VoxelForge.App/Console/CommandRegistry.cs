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
        EditorConfig config,
        ReferenceModelRegistry refRegistry,
        ReferenceModelLoader refLoader,
        ReferenceImageStore imageStore,
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
            new SaveCommand(loggerFactory),
            new LoadCommand(loggerFactory),
            new ListFilesCommand(),
            new ClearCommand(),
            new GridCommand(),
            new ConfigCommand(config),
            new RefLoadCommand(refRegistry, refLoader),
            new RefListCommand(refRegistry),
            new RefRemoveCommand(refRegistry),
            new RefTransformCommand(refRegistry),
            new RefModeCommand(refRegistry),
            new RefVisibilityCommand(refRegistry, show: true),
            new RefVisibilityCommand(refRegistry, show: false),
            new RefInfoCommand(refRegistry, refLoader),
            new ImgLoadCommand(imageStore),
            new ImgListCommand(imageStore),
            new ImgRemoveCommand(imageStore),
            new ScreenshotCommand(screenshotFactory ?? (() => null)),
            new VoxelizeCommand(refRegistry, loggerFactory),
        };

        var router = new CommandRouter(commands, logger);
        var allCommands = new List<IConsoleCommand>(commands) { new HelpCommand(router) };
        router = new CommandRouter(allCommands, logger);

        return router;
    }
}
