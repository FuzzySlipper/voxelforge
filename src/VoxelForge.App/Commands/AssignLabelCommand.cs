using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class AssignLabelCommand : IEditorCommand
{
    private readonly LabelIndex _labels;
    private readonly RegionId _regionId;
    private readonly string _regionName;
    private readonly IReadOnlyList<Point3> _voxels;
    private readonly Dictionary<Point3, RegionId?> _previousAssignments;
    private readonly bool _targetRegionExisted;

    public string Description => $"Assign {_voxels.Count} voxels to {_regionId}";

    public AssignLabelCommand(LabelIndex labels, RegionId regionId, IReadOnlyList<Point3> voxels)
        : this(labels, regionId, regionId.Value, voxels)
    {
    }

    public AssignLabelCommand(LabelIndex labels, RegionId regionId, string regionName, IReadOnlyList<Point3> voxels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(regionName);
        ArgumentNullException.ThrowIfNull(voxels);

        _labels = labels;
        _regionId = regionId;
        _regionName = regionName;
        _voxels = voxels;
        _targetRegionExisted = labels.Regions.ContainsKey(regionId);

        _previousAssignments = [];
        foreach (var pos in voxels)
            _previousAssignments[pos] = labels.GetRegion(pos);
    }

    public void Execute()
    {
        if (!_labels.Regions.ContainsKey(_regionId))
        {
            _labels.AddOrUpdateRegion(new RegionDef
            {
                Id = _regionId,
                Name = _regionName,
            });
        }

        _labels.AssignRegion(_regionId, _voxels);
    }

    public void Undo()
    {
        foreach (var pos in _voxels)
            _labels.RemoveFromRegion(pos);

        foreach (var entry in _previousAssignments)
        {
            if (entry.Value.HasValue)
                _labels.AssignRegion(entry.Value.Value, [entry.Key]);
        }

        if (!_targetRegionExisted)
            _labels.RemoveRegion(_regionId);
    }
}
