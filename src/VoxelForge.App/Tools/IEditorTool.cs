using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Tools;

public interface IEditorTool
{
    void OnMouseDown(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events);
    void OnMouseMove(RaycastHit? hit, EditorState state);
    void OnMouseUp(RaycastHit? hit, EditorState state, UndoStack undo, IEventPublisher events);
}
