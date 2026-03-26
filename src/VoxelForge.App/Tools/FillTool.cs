using VoxelForge.App.Commands;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public sealed class FillTool : IEditorTool
{
    public void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo)
    {
        if (hit is null) return;

        var startPos = hit.Value.VoxelPos;
        var startValue = state.ActiveModel.GetVoxel(startPos);
        if (!startValue.HasValue) return;

        var targetIndex = state.ActivePaletteIndex;
        if (startValue.Value == targetIndex) return;

        // BFS flood fill — same palette index boundary
        var visited = new HashSet<Point3>();
        var queue = new Queue<Point3>();
        var commands = new List<IEditorCommand>();

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
            commands.Add(new SetVoxelCommand(state.ActiveModel, pos, targetIndex));

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

        if (commands.Count > 0)
            undo.Execute(new CompoundCommand(commands, $"Fill {commands.Count} voxels"));
    }

    public void OnMouseMove(RaycastHit? hit, EditorState state) { }
    public void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo) { }
}
