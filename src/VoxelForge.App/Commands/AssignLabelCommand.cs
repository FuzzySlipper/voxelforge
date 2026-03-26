using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class AssignLabelCommand : IEditorCommand
{
    private readonly LabelIndex _labels;
    private readonly RegionId _regionId;
    private readonly IReadOnlyList<Point3> _voxels;
    private readonly Dictionary<Point3, RegionId?> _previousAssignments;

    public string Description => $"Assign {_voxels.Count} voxels to {_regionId}";

    public AssignLabelCommand(LabelIndex labels, RegionId regionId, IReadOnlyList<Point3> voxels)
    {
        _labels = labels;
        _regionId = regionId;
        _voxels = voxels;

        _previousAssignments = [];
        foreach (var pos in voxels)
            _previousAssignments[pos] = labels.GetRegion(pos);
    }

    public void Execute() => _labels.AssignRegion(_regionId, _voxels);

    public void Undo()
    {
        // Remove all voxels from the target region first
        foreach (var pos in _voxels)
            _labels.RemoveFromRegion(pos);

        // Restore previous assignments
        foreach (var (pos, oldRegion) in _previousAssignments)
        {
            if (oldRegion.HasValue)
                _labels.AssignRegion(oldRegion.Value, [pos]);
        }
    }
}
