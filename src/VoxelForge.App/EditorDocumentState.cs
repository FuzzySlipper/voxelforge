using VoxelForge.Core;

namespace VoxelForge.App;

/// <summary>
/// Durable mutable state for the currently open voxel document.
/// </summary>
public sealed class EditorDocumentState
{
    public EditorDocumentState(VoxelModel model, LabelIndex labels, List<AnimationClip>? clips = null)
    {
        Model = model;
        Labels = labels;
        Clips = clips ?? [];
    }

    /// <summary>
    /// Source-of-truth voxel model for the open document.
    /// </summary>
    public VoxelModel Model { get; }

    /// <summary>
    /// Source-of-truth semantic label index for the open document.
    /// </summary>
    public LabelIndex Labels { get; }

    /// <summary>
    /// Source-of-truth animation clips for the open document.
    /// </summary>
    public List<AnimationClip> Clips { get; }
}
