using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class PlaceTool : IEditorTool
{
    private readonly VoxelEditingService _editingService;

    public PlaceTool(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events)
    {
        if (hit is null) return;
        var newPos = new Point3(
            hit.Value.VoxelPos.X + hit.Value.FaceNormal.X,
            hit.Value.VoxelPos.Y + hit.Value.FaceNormal.Y,
            hit.Value.VoxelPos.Z + hit.Value.FaceNormal.Z);
        _editingService.SetVoxel(
            state.Document,
            undo,
            events,
            new SetVoxelRequest(newPos, state.ActivePaletteIndex));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events) { }
}
