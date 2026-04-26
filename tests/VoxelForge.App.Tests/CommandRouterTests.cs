using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class CommandRouterTests
{
    [Fact]
    public void Execute_WithCommandNameAndArgs_DoesNotRequireCommandLineRebuild()
    {
        var command = new RecordingConsoleCommand();
        var router = new CommandRouter([command], NullLogger<CommandRouter>.Instance);
        var context = CreateContext();

        var result = router.Execute("record", ["hello world", "2"], context);

        Assert.True(result.Success);
        Assert.Equal("hello world|2", result.Message);
        Assert.Equal(["hello world", "2"], command.LastArgs);
    }

    private static CommandContext CreateContext()
    {
        var events = new ApplicationEventDispatcher();
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        return new CommandContext
        {
            Document = new EditorDocumentState(model, labels),
            UndoStack = new UndoStack(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events),
            Events = events,
        };
    }

    private sealed class RecordingConsoleCommand : IConsoleCommand
    {
        public string Name => "record";
        public string[] Aliases => [];
        public string HelpText => "record";
        public string[] LastArgs { get; private set; } = [];

        public CommandResult Execute(string[] args, CommandContext context)
        {
            LastArgs = args;
            return CommandResult.Ok(string.Join("|", args));
        }
    }
}
