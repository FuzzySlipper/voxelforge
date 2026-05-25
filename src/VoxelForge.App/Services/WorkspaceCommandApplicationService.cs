using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service for workspace command execution and undo/redo.
/// Operates over explicit <see cref="VoxelForgeWorkspaceState"/> arguments.
/// <para>
/// Interprets typed command intents from adapters (MCP tools, Electron UI),
/// applies model/document/reference changes through existing App services
/// and undoable commands, and returns emitted <see cref="IApplicationEvent"/> facts.
/// </para>
/// </summary>
public sealed class WorkspaceCommandApplicationService
{
    private readonly VoxelEditingService _voxelEditingService;

    public WorkspaceCommandApplicationService(VoxelEditingService voxelEditingService)
    {
        ArgumentNullException.ThrowIfNull(voxelEditingService);
        _voxelEditingService = voxelEditingService;
    }

    /// <summary>
    /// Execute a command request against the workspace. Adapters parse transport-specific
    /// input and call this service; they should not duplicate mutation sequencing.
    /// </summary>
    public ApplicationServiceResult<WorkspaceCommandResult> Execute(
        VoxelForgeWorkspaceState workspace,
        WorkspaceCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        switch (request.CommandName.ToLowerInvariant())
        {
            case "undo":
                return Undo(workspace);

            case "redo":
                return Redo(workspace);

            case "set_voxel":
            case "remove_voxel":
            case "paint_voxel":
            case "fill":
                return ExecuteVoxelEdit(workspace, request);

            default:
                return new ApplicationServiceResult<WorkspaceCommandResult>
                {
                    Success = false,
                    Message = $"Unknown command: {request.CommandName}",
                    Data = new WorkspaceCommandResult { Revision = workspace.Revision },
                };
        }
    }

    /// <summary>
    /// Undo the last command in the workspace's undo stack.
    /// </summary>
    public ApplicationServiceResult<WorkspaceCommandResult> Undo(
        VoxelForgeWorkspaceState workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.UndoHistory.UndoCount == 0)
        {
            return new ApplicationServiceResult<WorkspaceCommandResult>
            {
                Success = false,
                Message = "Nothing to undo.",
                Data = new WorkspaceCommandResult { Revision = workspace.Revision },
            };
        }

        workspace.UndoStack.Undo();
        workspace.IncrementRevision();
        workspace.IsDirty = true;

        return new ApplicationServiceResult<WorkspaceCommandResult>
        {
            Success = true,
            Message = "Undo completed.",
            Data = new WorkspaceCommandResult { Revision = workspace.Revision },
        };
    }

    /// <summary>
    /// Redo the last undone command in the workspace's undo stack.
    /// </summary>
    public ApplicationServiceResult<WorkspaceCommandResult> Redo(
        VoxelForgeWorkspaceState workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        if (workspace.UndoHistory.RedoCount == 0)
        {
            return new ApplicationServiceResult<WorkspaceCommandResult>
            {
                Success = false,
                Message = "Nothing to redo.",
                Data = new WorkspaceCommandResult { Revision = workspace.Revision },
            };
        }

        workspace.UndoStack.Redo();
        workspace.IncrementRevision();
        workspace.IsDirty = true;

        return new ApplicationServiceResult<WorkspaceCommandResult>
        {
            Success = true,
            Message = "Redo completed.",
            Data = new WorkspaceCommandResult { Revision = workspace.Revision },
        };
    }

    private ApplicationServiceResult<WorkspaceCommandResult> ExecuteVoxelEdit(
        VoxelForgeWorkspaceState workspace,
        WorkspaceCommandRequest request)
    {
        // Translate to existing voxel editing commands if coordinates are present
        if (request.Arguments.TryGetValue("x", out var xObj) &&
            request.Arguments.TryGetValue("y", out var yObj) &&
            request.Arguments.TryGetValue("z", out var zObj) &&
            xObj is int x && yObj is int y && zObj is int z)
        {
            var point = new Point3(x, y, z);

            IEditorCommand? command = request.CommandName.ToLowerInvariant() switch
            {
                "set_voxel" when request.Arguments.TryGetValue("color_index", out var ciObj) && ciObj is byte ci
                    => new SetVoxelCommand(workspace.Document.Model, point, ci),
                "remove_voxel" => new RemoveVoxelCommand(workspace.Document.Model, point),
                "paint_voxel" when request.Arguments.TryGetValue("color_index", out var piObj) && piObj is byte pi
                    => new PaintVoxelCommand(workspace.Document.Model, point, pi),
                _ => null,
            };

            if (command is not null)
            {
                workspace.UndoStack.Execute(command);
                workspace.IncrementRevision();
                workspace.IsDirty = true;

                return new ApplicationServiceResult<WorkspaceCommandResult>
                {
                    Success = true,
                    Message = $"Executed {request.CommandName} at ({x}, {y}, {z}).",
                    Data = new WorkspaceCommandResult { Revision = workspace.Revision },
                };
            }
        }

        return new ApplicationServiceResult<WorkspaceCommandResult>
        {
            Success = false,
            Message = $"Cannot execute {request.CommandName}: insufficient or invalid arguments.",
            Data = new WorkspaceCommandResult { Revision = workspace.Revision },
        };
    }
}

/// <summary>
/// A command intent from a transport adapter (MCP tool, Electron UI).
/// </summary>
public sealed class WorkspaceCommandRequest
{
    /// <summary>Command name, e.g., "set_voxel", "undo", "redo".</summary>
    public required string CommandName { get; init; } = "";

    /// <summary>Named arguments for the command.</summary>
    public Dictionary<string, object?> Arguments { get; init; } = [];
}

/// <summary>
/// Result of executing a workspace command.
/// </summary>
public sealed class WorkspaceCommandResult
{
    /// <summary>Workspace revision after the command.</summary>
    public required long Revision { get; init; }
}
