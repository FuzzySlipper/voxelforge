using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class ToolPanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly Dictionary<EditorTool, Button> _buttons = [];

    public Widget Root => _root;

    public ToolPanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 2 };

        _root.Widgets.Add(new Label { Text = "Tools", TextColor = Color.White });

        foreach (var tool in Enum.GetValues<EditorTool>())
        {
            var button = new Button
            {
                Content = new Label { Text = $"{(int)tool + 1}: {tool}" },
                Width = 140,
            };
            var capturedTool = tool;
            button.Click += (_, _) =>
            {
                _state.ActiveTool = capturedTool;
                UpdateHighlights();
            };
            _buttons[tool] = button;
            _root.Widgets.Add(button);
        }

        UpdateHighlights();
    }

    public void UpdateHighlights()
    {
        foreach (var (tool, button) in _buttons)
        {
            button.Background = tool == _state.ActiveTool
                ? new SolidBrush(new Color(80, 120, 200))
                : new SolidBrush(new Color(60, 60, 60));
        }
    }
}
