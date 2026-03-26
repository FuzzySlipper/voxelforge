using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class PalettePanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly Grid _colorGrid;

    public Widget Root => _root;

    public PalettePanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "Palette", TextColor = Color.White });

        _colorGrid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
        for (int c = 0; c < 8; c++)
            _colorGrid.ColumnsProportions.Add(new Proportion(ProportionType.Pixels, 16));

        _root.Widgets.Add(_colorGrid);
        Refresh();
    }

    public void Refresh()
    {
        _colorGrid.Widgets.Clear();
        _colorGrid.RowsProportions.Clear();

        var entries = _state.ActiveModel.Palette.Entries.OrderBy(e => e.Key).ToList();
        int col = 0;
        int row = 0;

        foreach (var (index, mat) in entries)
        {
            if (col == 0)
                _colorGrid.RowsProportions.Add(new Proportion(ProportionType.Pixels, 16));

            var color = new Color(mat.Color.R, mat.Color.G, mat.Color.B, mat.Color.A);
            var btn = new Panel
            {
                Width = 14,
                Height = 14,
                Background = new SolidBrush(color),
            };

            var capturedIndex = index;
            btn.TouchDown += (_, _) =>
            {
                _state.ActivePaletteIndex = capturedIndex;
            };

            Grid.SetColumn(btn, col);
            Grid.SetRow(btn, row);
            _colorGrid.Widgets.Add(btn);

            col++;
            if (col >= 8) { col = 0; row++; }
        }
    }
}
