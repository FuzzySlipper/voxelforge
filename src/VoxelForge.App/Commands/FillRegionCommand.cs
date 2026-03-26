using VoxelForge.Core;

namespace VoxelForge.App.Commands;

public sealed class FillRegionCommand : IEditorCommand
{
    private readonly VoxelModel _model;
    private readonly Point3 _min;
    private readonly Point3 _max;
    private readonly byte _paletteIndex;
    private readonly Dictionary<Point3, byte?> _snapshot;

    public string Description => $"Fill region {_min} -> {_max}";

    public FillRegionCommand(VoxelModel model, Point3 min, Point3 max, byte paletteIndex)
    {
        _model = model;
        _min = min;
        _max = max;
        _paletteIndex = paletteIndex;

        // Snapshot affected positions
        _snapshot = [];
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            var pos = new Point3(x, y, z);
            _snapshot[pos] = model.GetVoxel(pos);
        }
    }

    public void Execute() => _model.FillRegion(_min, _max, _paletteIndex);

    public void Undo()
    {
        foreach (var (pos, oldValue) in _snapshot)
        {
            if (oldValue.HasValue)
                _model.SetVoxel(pos, oldValue.Value);
            else
                _model.RemoveVoxel(pos);
        }
    }
}
