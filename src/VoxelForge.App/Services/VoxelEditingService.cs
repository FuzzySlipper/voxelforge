using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.App.Services;

public readonly record struct SetVoxelRequest(Point3 Position, byte PaletteIndex);

public readonly record struct RemoveVoxelRequest(Point3 Position);

public readonly record struct FillVoxelRegionRequest(Point3 Min, Point3 Max, byte PaletteIndex);

public readonly record struct SetGridHintRequest(int Size);

public readonly record struct ApplyVoxelMutationIntentRequest(VoxelMutationIntent Intent);

/// <summary>
/// Stateless orchestration service for undoable voxel edits.
/// </summary>
public sealed class VoxelEditingService
{
    public ApplicationServiceResult SetVoxel(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        SetVoxelRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        undoStack.Execute(new SetVoxelCommand(document.Model, request.Position, request.PaletteIndex));
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.SetVoxel,
                $"Set ({request.Position.X},{request.Position.Y},{request.Position.Z}) = {request.PaletteIndex}",
                1),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Set ({request.Position.X},{request.Position.Y},{request.Position.Z}) = {request.PaletteIndex}",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult RemoveVoxel(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        RemoveVoxelRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        undoStack.Execute(new RemoveVoxelCommand(document.Model, request.Position));
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.RemoveVoxel,
                $"Removed ({request.Position.X},{request.Position.Y},{request.Position.Z})",
                1),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Removed ({request.Position.X},{request.Position.Y},{request.Position.Z})",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult FillRegion(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        FillVoxelRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        undoStack.Execute(new FillRegionCommand(document.Model, request.Min, request.Max, request.PaletteIndex));

        int count = (request.Max.X - request.Min.X + 1)
            * (request.Max.Y - request.Min.Y + 1)
            * (request.Max.Z - request.Min.Z + 1);
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.FillRegion,
                $"Filled region with index {request.PaletteIndex}",
                count),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Filled {count} voxels from ({request.Min.X},{request.Min.Y},{request.Min.Z}) to ({request.Max.X},{request.Max.Y},{request.Max.Z}) with index {request.PaletteIndex}",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult Clear(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        var positions = new List<Point3>();
        foreach (var position in document.Model.Voxels.Keys)
            positions.Add(position);

        if (positions.Count > 0)
        {
            var operations = new List<IEditorCommand>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
                operations.Add(new RemoveVoxelCommand(document.Model, positions[i]));
            undoStack.Execute(new CompoundCommand(operations, $"Clear {positions.Count} voxels"));
        }

        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.Clear,
                $"Cleared {positions.Count} voxels",
                positions.Count),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Cleared {positions.Count} voxels.",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult SetGridHint(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        SetGridHintRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        if (request.Size < 1 || request.Size > 256)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = "Invalid size. Expected integer 1-256.",
            };
        }

        int oldSize = document.Model.GridHint;
        undoStack.Execute(new SetGridHintCommand(document.Model, request.Size));
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.GridHintChanged,
                $"Grid hint changed from {oldSize} to {request.Size}",
                null),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Grid hint set to {request.Size}",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult ApplyMutationIntent(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        ApplyVoxelMutationIntentRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Intent);

        var operations = new List<IEditorCommand>(request.Intent.Assignments.Count);
        int setCount = 0;
        int removeCount = 0;
        for (int i = 0; i < request.Intent.Assignments.Count; i++)
        {
            var assignment = request.Intent.Assignments[i];
            if (assignment.PaletteIndex.HasValue)
            {
                operations.Add(new SetVoxelCommand(document.Model, assignment.Position, assignment.PaletteIndex.Value));
                setCount++;
            }
            else
            {
                operations.Add(new RemoveVoxelCommand(document.Model, assignment.Position));
                removeCount++;
            }
        }

        if (operations.Count > 0)
            undoStack.Execute(new CompoundCommand(operations, request.Intent.Description));

        var kind = setCount > 0 && removeCount == 0
            ? VoxelModelChangeKind.SetVoxel
            : VoxelModelChangeKind.RemoveVoxel;
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(kind, request.Intent.Description, operations.Count),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = request.Intent.Description,
            Events = applicationEvents,
        };
    }
}
