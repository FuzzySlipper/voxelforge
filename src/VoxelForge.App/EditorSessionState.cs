using VoxelForge.Core;

namespace VoxelForge.App;

/// <summary>
/// Durable mutable state for editor session choices that are not serialized in project files.
/// </summary>
public sealed class EditorSessionState
{
    public byte ActivePaletteIndex { get; set; } = 1;
    public EditorTool ActiveTool { get; set; } = EditorTool.Place;
    public RegionId? ActiveRegion { get; set; }
    public int ActiveFrameIndex { get; set; } = -1; // -1 = base model
    public HashSet<Point3> SelectedVoxels { get; } = [];
}
