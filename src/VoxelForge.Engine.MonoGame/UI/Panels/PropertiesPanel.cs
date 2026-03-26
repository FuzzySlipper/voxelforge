using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using VoxelForge.App;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class PropertiesPanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly Label _infoLabel;

    public Widget Root => _root;

    public PropertiesPanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "Properties", TextColor = Color.White });

        _infoLabel = new Label { Text = "", TextColor = Color.LightGray, Wrap = true };
        _root.Widgets.Add(_infoLabel);
    }

    public void Refresh()
    {
        var lines = new List<string>
        {
            $"Tool: {_state.ActiveTool}",
            $"Palette: {_state.ActivePaletteIndex}",
            $"Voxels: {_state.ActiveModel.GetVoxelCount()}",
            $"Grid: {_state.ActiveModel.GridHint}",
        };

        if (_state.ActiveRegion.HasValue)
            lines.Add($"Region: {_state.ActiveRegion.Value}");

        if (_state.ActiveFrameIndex >= 0)
            lines.Add($"Frame: {_state.ActiveFrameIndex}");
        else
            lines.Add("Frame: Base");

        if (_state.SelectedVoxels.Count > 0)
            lines.Add($"Selected: {_state.SelectedVoxels.Count}");

        _infoLabel.Text = string.Join("\n", lines);
    }
}
