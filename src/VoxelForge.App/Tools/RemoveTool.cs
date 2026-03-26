using VoxelForge.App.Commands;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class RemoveTool : IEditorTool
{
    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo)
    {
        if (hit is null) return;
        undo.Execute(new RemoveVoxelCommand(state.ActiveModel, hit.Value.VoxelPos));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo) { }
}
