using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

public readonly record struct RegionListEntry(string Name, int VoxelCount, RegionId? ParentId);

public readonly record struct CreateRegionRequest(string RegionName);

public readonly record struct AssignVoxelRegionRequest(string RegionName, Point3 Position);

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
            entries.Add(new RegionListEntry(entry.Value.Name, entry.Value.Voxels.Count, entry.Value.ParentId));

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

        undoStack.Execute(new CreateRegionCommand(labels, new RegionDef
        {
            Id = regionId,
            Name = request.RegionName,
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
}
