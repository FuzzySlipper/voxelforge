using Microsoft.Extensions.Logging;
using VoxelForge.Core;

namespace VoxelForge.App;

/// <summary>
/// Central editor state. All panels read from and write to this.
/// No static access — passed via constructor injection.
/// </summary>
public sealed class EditorState
{
    private readonly ILogger<EditorState> _logger;

    public VoxelModel ActiveModel { get; set; }
    public LabelIndex Labels { get; set; }
    public List<AnimationClip> Clips { get; set; } = [];
    public byte ActivePaletteIndex { get; set; } = 1;
    public EditorTool ActiveTool { get; set; } = EditorTool.Place;
    public RegionId? ActiveRegion { get; set; }
    public int ActiveFrameIndex { get; set; } = -1; // -1 = base model
    public HashSet<Point3> SelectedVoxels { get; set; } = [];

    /// <summary>
    /// Fired when the model data changes (voxel set/remove, label change, etc.).
    /// Renderer should mark dirty when this fires.
    /// </summary>
    public event Action? ModelChanged;

    public EditorState(VoxelModel model, LabelIndex labels, ILogger<EditorState> logger)
    {
        ActiveModel = model;
        Labels = labels;
        _logger = logger;
    }

    public void NotifyModelChanged()
    {
        _logger.LogTrace("ModelChanged event fired");
        ModelChanged?.Invoke();
    }
}
