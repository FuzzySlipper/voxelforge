using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class LabelTool : IEditorTool
{
    private readonly RegionEditingService _regionEditingService;

    public LabelTool(RegionEditingService regionEditingService)
    {
        _regionEditingService = regionEditingService;
    }

    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events)
    {
        if (hit is null) return;
        if (!state.ActiveRegion.HasValue) return;

        _regionEditingService.AssignVoxel(
            state.Document,
            undo,
            events,
            new AssignVoxelRegionRequest(state.ActiveRegion.Value.Value, hit.Value.VoxelPos));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events) { }
}
