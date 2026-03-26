namespace VoxelForge.App.Tools;

/// <summary>
/// Maps EditorTool enum values to IEditorTool implementations.
/// No reflection — explicit registration.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<EditorTool, IEditorTool> _tools = new()
    {
        [EditorTool.Place] = new PlaceTool(),
        [EditorTool.Remove] = new RemoveTool(),
        [EditorTool.Paint] = new PaintTool(),
        [EditorTool.Select] = new SelectTool(),
        [EditorTool.Fill] = new FillTool(),
        [EditorTool.Label] = new LabelTool(),
    };

    public IEditorTool Get(EditorTool tool) => _tools[tool];
}
