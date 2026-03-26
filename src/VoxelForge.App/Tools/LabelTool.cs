using VoxelForge.App.Commands;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class LabelTool : IEditorTool
{
    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo)
    {
        if (hit is null) return;
        if (!state.ActiveRegion.HasValue) return;
        if (state.ActiveModel.GetVoxel(hit.Value.VoxelPos) is null) return;

        undo.Execute(new AssignLabelCommand(
            state.Labels, state.ActiveRegion.Value, [hit.Value.VoxelPos]));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo) { }
}
