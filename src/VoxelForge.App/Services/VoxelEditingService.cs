using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.App.Services;

public readonly record struct SetVoxelRequest(Point3 Position, byte PaletteIndex);

public readonly record struct RemoveVoxelRequest(Point3 Position);

public readonly record struct RemoveVoxelsRequest(IReadOnlyCollection<Point3> Positions, string Description);

public readonly record struct PaintVoxelRequest(Point3 Position, byte PaletteIndex);

public readonly record struct FloodFillVoxelRequest(Point3 Start, byte PaletteIndex);

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

        var removedRegion = document.Labels.GetRegion(request.Position);
        undoStack.Execute(new RemoveVoxelCommand(document.Model, document.Labels, request.Position));

        var applicationEvents = new List<IApplicationEvent>
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.RemoveVoxel,
                $"Removed ({request.Position.X},{request.Position.Y},{request.Position.Z})",
                1),
        };
        if (removedRegion.HasValue)
        {
            applicationEvents.Add(new LabelChangedEvent(
                LabelChangeKind.RegionUnassigned,
                $"Removed label from ({request.Position.X},{request.Position.Y},{request.Position.Z})",
                removedRegion.Value,
                1));
        }
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Removed ({request.Position.X},{request.Position.Y},{request.Position.Z})",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult RemoveVoxels(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        RemoveVoxelsRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Positions);
        ArgumentNullException.ThrowIfNull(request.Description);

        var operations = new List<IEditorCommand>(request.Positions.Count);
        int removedLabelCount = 0;
        foreach (var position in request.Positions)
        {
            if (document.Labels.GetRegion(position).HasValue)
                removedLabelCount++;
            operations.Add(new RemoveVoxelCommand(document.Model, document.Labels, position));
        }

        if (operations.Count > 0)
            undoStack.Execute(new CompoundCommand(operations, request.Description));

        var applicationEvents = new List<IApplicationEvent>
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.RemoveVoxel,
                request.Description,
                operations.Count),
        };
        if (removedLabelCount > 0)
        {
            applicationEvents.Add(new LabelChangedEvent(
                LabelChangeKind.RegionUnassigned,
                $"Removed {removedLabelCount} label assignment(s)",
                null,
                removedLabelCount));
        }
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = request.Description,
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult PaintVoxel(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        PaintVoxelRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        undoStack.Execute(new PaintVoxelCommand(document.Model, request.Position, request.PaletteIndex));
        var applicationEvents = new IApplicationEvent[]
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.SetVoxel,
                $"Painted ({request.Position.X},{request.Position.Y},{request.Position.Z}) = {request.PaletteIndex}",
                1),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Painted ({request.Position.X},{request.Position.Y},{request.Position.Z}) = {request.PaletteIndex}",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult FloodFill(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        FloodFillVoxelRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);

        var startValue = document.Model.GetVoxel(request.Start);
        if (!startValue.HasValue)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Cannot flood fill air at ({request.Start.X},{request.Start.Y},{request.Start.Z}).",
            };
        }

        if (startValue.Value == request.PaletteIndex)
        {
            return new ApplicationServiceResult
            {
                Success = true,
                Message = "Flood fill target already has the requested palette index.",
            };
        }

        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();
        var assignments = new List<VoxelAssignment>();

        queue.Enqueue(request.Start);
        visited.Add(request.Start);

        Point3[] neighbors =
        [
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        ];

        while (queue.Count > 0)
        {
            var position = queue.Dequeue();
            assignments.Add(new VoxelAssignment(position, request.PaletteIndex));

            foreach (var offset in neighbors)
            {
                var neighbor = new Point3(position.X + offset.X, position.Y + offset.Y, position.Z + offset.Z);
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);

                var neighborValue = document.Model.GetVoxel(neighbor);
                if (neighborValue.HasValue && neighborValue.Value == startValue.Value)
                    queue.Enqueue(neighbor);
            }
        }

        return ApplyMutationIntent(
            document,
            undoStack,
            events,
            new ApplyVoxelMutationIntentRequest(new VoxelMutationIntent
            {
                Assignments = assignments,
                Description = $"Fill {assignments.Count} voxels",
            }));
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

        int removedLabelCount = 0;
        for (int i = 0; i < positions.Count; i++)
        {
            if (document.Labels.GetRegion(positions[i]).HasValue)
                removedLabelCount++;
        }

        if (positions.Count > 0)
        {
            var operations = new List<IEditorCommand>(positions.Count);
            for (int i = 0; i < positions.Count; i++)
                operations.Add(new RemoveVoxelCommand(document.Model, document.Labels, positions[i]));
            undoStack.Execute(new CompoundCommand(operations, $"Clear {positions.Count} voxels"));
        }

        var applicationEvents = new List<IApplicationEvent>
        {
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.Clear,
                $"Cleared {positions.Count} voxels",
                positions.Count),
        };
        if (removedLabelCount > 0)
        {
            applicationEvents.Add(new LabelChangedEvent(
                LabelChangeKind.RegionUnassigned,
                $"Cleared {removedLabelCount} label assignment(s)",
                null,
                removedLabelCount));
        }
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
        int removedLabelCount = 0;
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
                if (document.Labels.GetRegion(assignment.Position).HasValue)
                    removedLabelCount++;
                operations.Add(new RemoveVoxelCommand(document.Model, document.Labels, assignment.Position));
                removeCount++;
            }
        }

        if (operations.Count > 0)
            undoStack.Execute(new CompoundCommand(operations, request.Intent.Description));

        var kind = setCount > 0 && removeCount == 0
            ? VoxelModelChangeKind.SetVoxel
            : removeCount > 0 && setCount == 0
                ? VoxelModelChangeKind.RemoveVoxel
                : VoxelModelChangeKind.MixedVoxelEdit;
        var applicationEvents = new List<IApplicationEvent>
        {
            new VoxelModelChangedEvent(kind, request.Intent.Description, operations.Count),
        };
        if (removedLabelCount > 0)
        {
            applicationEvents.Add(new LabelChangedEvent(
                LabelChangeKind.RegionUnassigned,
                $"Removed {removedLabelCount} label assignment(s)",
                null,
                removedLabelCount));
        }
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = request.Intent.Description,
            Events = applicationEvents,
        };
    }
}
