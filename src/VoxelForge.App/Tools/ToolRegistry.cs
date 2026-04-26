using VoxelForge.App.Services;

namespace VoxelForge.App.Tools;

/// <summary>
/// Maps EditorTool enum values to IEditorTool implementations.
/// No reflection — explicit registration.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<EditorTool, IEditorTool> _tools;

    public ToolRegistry(VoxelEditingService voxelEditingService, RegionEditingService regionEditingService)
    {
        _tools = new Dictionary<EditorTool, IEditorTool>
        {
            [EditorTool.Place] = new PlaceTool(voxelEditingService),
            [EditorTool.Remove] = new RemoveTool(voxelEditingService),
            [EditorTool.Paint] = new PaintTool(voxelEditingService),
            [EditorTool.Select] = new SelectTool(),
            [EditorTool.Fill] = new FillTool(voxelEditingService),
            [EditorTool.Label] = new LabelTool(regionEditingService),
        };
    }

    public IEditorTool Get(EditorTool tool) => _tools[tool];
}
