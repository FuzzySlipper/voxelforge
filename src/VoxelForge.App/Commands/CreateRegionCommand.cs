using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Creates or replaces a region definition, preserving the previous definition for undo.
/// </summary>
public sealed class CreateRegionCommand : IEditorCommand
{
    private readonly LabelIndex _labels;
    private readonly RegionDef _newRegion;
    private readonly RegionDef? _oldRegion;
    private readonly bool _hadOldRegion;

    public string Description => $"Create region {_newRegion.Id}";

    public CreateRegionCommand(LabelIndex labels, RegionDef newRegion)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(newRegion);

        _labels = labels;
        _newRegion = CloneRegion(newRegion);
        if (labels.Regions.TryGetValue(newRegion.Id, out var oldRegion))
        {
            _hadOldRegion = true;
            _oldRegion = CloneRegion(oldRegion);
        }
    }

    public void Execute()
    {
        _labels.AddOrUpdateRegion(CloneRegion(_newRegion));
    }

    public void Undo()
    {
        if (_hadOldRegion && _oldRegion is not null)
            _labels.AddOrUpdateRegion(CloneRegion(_oldRegion));
        else
            _labels.RemoveRegion(_newRegion.Id);
    }

    internal static RegionDef CloneRegion(RegionDef region)
    {
        return new RegionDef
        {
            Id = region.Id,
            Name = region.Name,
            ParentId = region.ParentId,
            Voxels = new HashSet<Point3>(region.Voxels),
            Properties = new Dictionary<string, string>(region.Properties),
        };
    }
}
