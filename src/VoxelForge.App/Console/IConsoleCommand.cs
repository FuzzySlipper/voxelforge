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
/// Shared state passed to every command. Commands never access global state.
/// </summary>
public sealed class CommandContext
{
    public required VoxelForge.Core.VoxelModel Model { get; init; }
    public required VoxelForge.Core.LabelIndex Labels { get; init; }
    public required List<VoxelForge.Core.AnimationClip> Clips { get; init; }
    public required VoxelForge.App.Commands.UndoStack UndoStack { get; init; }

    /// <summary>
    /// Fired after any command mutates the model so the renderer can update.
    /// </summary>
    public Action? OnModelChanged { get; set; }
}
