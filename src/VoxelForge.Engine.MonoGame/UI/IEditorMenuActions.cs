namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// View-runtime actions invoked by the menu bar that are not application model mutations.
/// </summary>
public interface IEditorMenuActions
{
    void ExitApplication();
    void SnapFront();
    void SnapSide();
    void SnapTop();
    void ToggleWireframe();
}
