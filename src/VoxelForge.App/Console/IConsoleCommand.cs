using VoxelForge.App.Events;

namespace VoxelForge.App.Console;

/// <summary>
/// A command that can be executed from the debug console or via stdio.
/// Backed by the same tool handlers the LLM uses.
/// </summary>
public interface IConsoleCommand
{
    string Name { get; }
    string[] Aliases { get; }
    string HelpText { get; }
    CommandResult Execute(string[] args, CommandContext context);
}

public sealed class CommandResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public object? Data { get; init; }

    public static CommandResult Ok(string message, object? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static CommandResult Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// How the console session was invoked — commands can branch on this
/// to skip side-effects (e.g. file writes) that don't make sense for
/// a particular mode.
/// </summary>
public enum ExecutionMode
{
    Interactive,
    Stdio,
    Headless,
}

/// <summary>
/// Shared state passed to every command. Commands never access global state.
/// </summary>
public sealed class CommandContext
{
    public required EditorDocumentState Document { get; init; }
    public VoxelForge.Core.VoxelModel Model => Document.Model;
    public VoxelForge.Core.LabelIndex Labels => Document.Labels;
    public List<VoxelForge.Core.AnimationClip> Clips => Document.Clips;
    public required VoxelForge.App.Commands.UndoStack UndoStack { get; init; }
    public required IEventPublisher Events { get; init; }
    public ExecutionMode Mode { get; set; } = ExecutionMode.Interactive;
}
