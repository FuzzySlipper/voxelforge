using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class RemoveTool : IEditorTool
{
    private readonly VoxelEditingService _editingService;

    public RemoveTool(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events)
    {
        if (hit is null) return;
        _editingService.RemoveVoxel(
            state.Document,
            undo,
            events,
            new RemoveVoxelRequest(hit.Value.VoxelPos));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events) { }
}
