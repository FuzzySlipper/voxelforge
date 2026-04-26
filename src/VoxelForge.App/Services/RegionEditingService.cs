using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

public readonly record struct RegionListEntry(string Name, int VoxelCount, RegionId? ParentId);

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

    public ApplicationServiceResult AssignVoxel(
        LabelIndex labels,
        IEventPublisher events,
        AssignVoxelRegionRequest request)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.RegionName);

        var regionId = new RegionId(request.RegionName);
        bool createdRegion = false;
        if (!labels.Regions.ContainsKey(regionId))
        {
            labels.AddOrUpdateRegion(new RegionDef { Id = regionId, Name = request.RegionName });
            createdRegion = true;
        }

        labels.AssignRegion(regionId, [request.Position]);

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
