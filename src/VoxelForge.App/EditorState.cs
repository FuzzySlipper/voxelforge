using Microsoft.Extensions.Logging;
using VoxelForge.Core;

namespace VoxelForge.App;

/// <summary>
/// Transitional aggregate for editor state. Existing panels and tools read through this facade
/// while durable mutable data lives in explicit state objects.
/// </summary>
public sealed class EditorState
{
    private readonly ILogger<EditorState> _logger;

    public EditorDocumentState Document { get; }
    public EditorSessionState Session { get; }

    public VoxelModel ActiveModel => Document.Model;
    public LabelIndex Labels => Document.Labels;
    public List<AnimationClip> Clips => Document.Clips;

    public byte ActivePaletteIndex
    {
        get => Session.ActivePaletteIndex;
        set => Session.ActivePaletteIndex = value;
    }

    public EditorTool ActiveTool
    {
        get => Session.ActiveTool;
        set => Session.ActiveTool = value;
    }

    public RegionId? ActiveRegion
    {
        get => Session.ActiveRegion;
        set => Session.ActiveRegion = value;
    }

    public int ActiveFrameIndex
    {
        get => Session.ActiveFrameIndex;
        set => Session.ActiveFrameIndex = value;
    }

    public HashSet<Point3> SelectedVoxels => Session.SelectedVoxels;

    /// <summary>
    /// Fired when the model data changes (voxel set/remove, label change, etc.).
    /// Renderer should mark dirty when this fires.
    /// </summary>
    public event Action? ModelChanged;

    public EditorState(EditorDocumentState document, EditorSessionState session, ILogger<EditorState> logger)
    {
        Document = document;
        Session = session;
        _logger = logger;
    }

    public EditorState(VoxelModel model, LabelIndex labels, ILogger<EditorState> logger)
        : this(new EditorDocumentState(model, labels), new EditorSessionState(), logger)
    {
    }

    public void NotifyModelChanged()
    {
        _logger.LogTrace("ModelChanged event fired");
        ModelChanged?.Invoke();
    }
}
