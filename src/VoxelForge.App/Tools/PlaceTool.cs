using VoxelForge.App.Commands;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class PlaceTool : IEditorTool
{
    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo)
    {
        if (hit is null) return;
        var newPos = new Point3(
            hit.Value.VoxelPos.X + hit.Value.FaceNormal.X,
            hit.Value.VoxelPos.Y + hit.Value.FaceNormal.Y,
            hit.Value.VoxelPos.Z + hit.Value.FaceNormal.Z);
        undo.Execute(new SetVoxelCommand(state.ActiveModel, newPos, state.ActivePaletteIndex));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo) { }
}
