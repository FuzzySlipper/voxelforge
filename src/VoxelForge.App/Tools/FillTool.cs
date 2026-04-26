using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Services;

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

        var startPos = hit.Value.VoxelPos;
        var startValue = state.ActiveModel.GetVoxel(startPos);
        if (!startValue.HasValue) return;

        var targetIndex = state.ActivePaletteIndex;
        if (startValue.Value == targetIndex) return;

        // BFS flood fill — same palette index boundary.
        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();
        var assignments = new List<VoxelAssignment>();

        queue.Enqueue(startPos);
        visited.Add(startPos);

        Point3[] neighbors =
        [
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        ];

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            assignments.Add(new VoxelAssignment(pos, targetIndex));

            foreach (var offset in neighbors)
            {
                var neighbor = new Point3(pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z);
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);

                var neighborValue = state.ActiveModel.GetVoxel(neighbor);
                if (neighborValue.HasValue && neighborValue.Value == startValue.Value)
                    queue.Enqueue(neighbor);
            }
        }

        if (assignments.Count == 0)
            return;

        _editingService.ApplyMutationIntent(
            state.Document,
            undo,
            events,
            new ApplyVoxelMutationIntentRequest(new VoxelMutationIntent
            {
                Assignments = assignments,
                Description = $"Fill {assignments.Count} voxels",
            }));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events) { }
}
