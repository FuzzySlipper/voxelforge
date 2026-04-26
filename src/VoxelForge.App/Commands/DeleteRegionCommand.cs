using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Removes a region definition and its label assignments without changing voxels.
/// </summary>
public sealed class DeleteRegionCommand : IEditorCommand
{
    private readonly LabelIndex _labels;
    private readonly RegionDef _deletedRegion;

    public string Description => $"Delete region {_deletedRegion.Id}";

    public DeleteRegionCommand(LabelIndex labels, RegionId regionId)
    {
        ArgumentNullException.ThrowIfNull(labels);

        if (!labels.Regions.TryGetValue(regionId, out var region))
            throw new ArgumentException($"Region '{regionId}' does not exist.", nameof(regionId));

        _labels = labels;
        _deletedRegion = CreateRegionCommand.CloneRegion(region);
    }

    public void Execute()
    {
        _labels.RemoveRegion(_deletedRegion.Id);
    }

    public void Undo()
    {
        var restored = CreateRegionCommand.CloneRegion(_deletedRegion);
        var voxels = new List<Point3>(restored.Voxels);
        _labels.AddOrUpdateRegion(restored);
        _labels.AssignRegion(restored.Id, voxels);
    }
}
