using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class FillTool : IEditorTool
{
    private readonly VoxelEditingService _editingService;

    public FillTool(VoxelEditingService editingService)
    {
        _editingService = editingService;
    }

    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events)
    {
        if (hit is null) return;

        _editingService.FloodFill(
            state.Document,
            undo,
            events,
            new FloodFillVoxelRequest(hit.Value.VoxelPos, state.ActivePaletteIndex));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events) { }
}
