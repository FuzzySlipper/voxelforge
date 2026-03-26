using VoxelForge.App.Commands;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class SelectTool : IEditorTool
{
    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo)
    {
        if (hit is null)
        {
            state.SelectedVoxels.Clear();
            return;
        }
        state.SelectedVoxels.Add(hit.Value.VoxelPos);
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state)
    {
        // Accumulate during drag
        if (hit is not null)
            state.SelectedVoxels.Add(hit.Value.VoxelPos);
    }

    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo) { }
}
