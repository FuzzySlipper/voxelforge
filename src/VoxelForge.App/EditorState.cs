using VoxelForge.Core;

namespace VoxelForge.App;

/// <summary>
/// Transitional aggregate for editor state. Existing panels and tools read through this facade
/// while durable mutable data lives in explicit state objects.
/// </summary>
public sealed class EditorState
{
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

    public EditorState(EditorDocumentState document, EditorSessionState session)
    {
        Document = document;
        Session = session;
    }

    public EditorState(VoxelModel model, LabelIndex labels)
        : this(new EditorDocumentState(model, labels), new EditorSessionState())
    {
    }
}
