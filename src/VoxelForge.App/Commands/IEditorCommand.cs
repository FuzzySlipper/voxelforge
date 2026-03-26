namespace VoxelForge.App.Commands;

public interface IEditorCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
