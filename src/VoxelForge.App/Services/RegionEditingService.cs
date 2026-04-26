using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

public readonly record struct RegionListEntry(RegionId Id, string Name, int VoxelCount, RegionId? ParentId);

public readonly record struct CreateRegionRequest(
    string RegionName,
    RegionId? ParentId = null,
    IReadOnlyDictionary<string, string>? Properties = null);

public readonly record struct DeleteRegionRequest(string RegionName);

public readonly record struct AssignVoxelRegionRequest(string RegionName, Point3 Position);

public readonly record struct AssignVoxelsRegionRequest(string RegionName, IReadOnlyList<Point3> Positions);

/// <summary>
/// Stateless service for semantic region edits and listing.
/// </summary>
public sealed class RegionEditingService
{
    public ApplicationServiceResult<IReadOnlyList<RegionListEntry>> ListRegions(LabelIndex labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var entries = new List<RegionListEntry>();
        foreach (var entry in labels.Regions)
            entries.Add(new RegionListEntry(entry.Key, entry.Value.Name, entry.Value.Voxels.Count, entry.Value.ParentId));

        return new ApplicationServiceResult<IReadOnlyList<RegionListEntry>>
        {
            Success = true,
            Message = entries.Count == 0 ? "No regions defined." : "Regions.",
            Data = entries,
        };
    }

    public ApplicationServiceResult CreateRegion(
        LabelIndex labels,
        UndoStack undoStack,
        IEventPublisher events,
        CreateRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.RegionName);

        if (string.IsNullOrWhiteSpace(request.RegionName))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = "Region name cannot be empty.",
            };
        }

        var regionId = new RegionId(request.RegionName);
        if (labels.Regions.ContainsKey(regionId))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Region '{request.RegionName}' already exists.",
            };
        }

        if (request.ParentId.HasValue && !labels.Regions.ContainsKey(request.ParentId.Value))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Parent region '{request.ParentId.Value}' does not exist.",
            };
        }

        undoStack.Execute(new CreateRegionCommand(labels, new RegionDef
        {
            Id = regionId,
            Name = request.RegionName,
            ParentId = request.ParentId,
            Properties = CloneProperties(request.Properties),
        }));

        var applicationEvents = new IApplicationEvent[]
        {
            new LabelChangedEvent(
                LabelChangeKind.RegionCreated,
                $"Created region '{request.RegionName}'",
                regionId,
                0),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Created region '{request.RegionName}'",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult DeleteRegion(
        LabelIndex labels,
        UndoStack undoStack,
        IEventPublisher events,
        DeleteRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.RegionName);

        var regionId = new RegionId(request.RegionName);
        if (!labels.Regions.TryGetValue(regionId, out var region))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Region '{request.RegionName}' does not exist.",
            };
        }

        foreach (var entry in labels.Regions)
        {
            if (entry.Value.ParentId == regionId)
            {
                return new ApplicationServiceResult
                {
                    Success = false,
                    Message = $"Cannot delete region '{request.RegionName}' while child region '{entry.Key}' exists.",
                };
            }
        }

        int voxelCount = region.Voxels.Count;
        undoStack.Execute(new DeleteRegionCommand(labels, regionId));

        var applicationEvents = new IApplicationEvent[]
        {
            new LabelChangedEvent(
                LabelChangeKind.RegionDeleted,
                $"Deleted region '{request.RegionName}'",
                regionId,
                voxelCount),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Deleted region '{request.RegionName}'",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult AssignVoxel(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        AssignVoxelRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.RegionName);

        if (document.Model.GetVoxel(request.Position) is null)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Cannot label air at ({request.Position.X},{request.Position.Y},{request.Position.Z}).",
            };
        }

        var regionId = new RegionId(request.RegionName);
        bool createdRegion = !document.Labels.Regions.ContainsKey(regionId);
        undoStack.Execute(new AssignLabelCommand(
            document.Labels,
            regionId,
            request.RegionName,
            [request.Position]));

        var applicationEvents = new List<IApplicationEvent>();
        if (createdRegion)
        {
            applicationEvents.Add(new LabelChangedEvent(
                LabelChangeKind.RegionCreated,
                $"Created region '{request.RegionName}'",
                regionId,
                0));
        }

        applicationEvents.Add(new LabelChangedEvent(
            LabelChangeKind.RegionAssigned,
            $"Labeled ({request.Position.X},{request.Position.Y},{request.Position.Z}) as '{request.RegionName}'",
            regionId,
            1));
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Labeled ({request.Position.X},{request.Position.Y},{request.Position.Z}) as '{request.RegionName}'",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult AssignVoxels(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        AssignVoxelsRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.RegionName);
        ArgumentNullException.ThrowIfNull(request.Positions);

        if (request.Positions.Count == 0)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = "No voxel positions were provided for region assignment.",
            };
        }

        var regionId = new RegionId(request.RegionName);
        if (!document.Labels.Regions.ContainsKey(regionId))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Region '{request.RegionName}' does not exist.",
            };
        }

        for (int i = 0; i < request.Positions.Count; i++)
        {
            var position = request.Positions[i];
            if (document.Model.GetVoxel(position) is null)
            {
                return new ApplicationServiceResult
                {
                    Success = false,
                    Message = $"Cannot label air at ({position.X},{position.Y},{position.Z}).",
                };
            }
        }

        undoStack.Execute(new AssignLabelCommand(
            document.Labels,
            regionId,
            request.RegionName,
            request.Positions));

        var applicationEvents = new IApplicationEvent[]
        {
            new LabelChangedEvent(
                LabelChangeKind.RegionAssigned,
                $"Assigned {request.Positions.Count} voxel(s) to region '{request.RegionName}'",
                regionId,
                request.Positions.Count),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Assigned {request.Positions.Count} voxel(s) to region '{request.RegionName}'",
            Events = applicationEvents,
        };
    }

    private static Dictionary<string, string> CloneProperties(IReadOnlyDictionary<string, string>? properties)
    {
        var clone = new Dictionary<string, string>(StringComparer.Ordinal);
        if (properties is null)
            return clone;

        foreach (var entry in properties)
            clone[entry.Key] = entry.Value;

        return clone;
    }
}
