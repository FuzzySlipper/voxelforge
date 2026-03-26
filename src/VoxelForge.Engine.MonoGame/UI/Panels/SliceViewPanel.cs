using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Tools;
using VoxelForge.Core;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// 2D cross-section view of the voxel model for debugging and precise editing.
/// Renders a single layer as a colored grid using SpriteBatch.
/// </summary>
public sealed class SliceViewPanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly Panel _canvas;

    private SliceAxis _sliceAxis = SliceAxis.Z;
    private int _layerIndex = 16;
    private const int CellSize = 8;

    private Texture2D? _pixelTexture;
    private GraphicsDevice? _graphicsDevice;
    private SpriteBatch? _spriteBatch;

    public Widget Root => _root;
    public bool Visible { get; set; }

    public SliceViewPanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 4 };

        // Header
        _root.Widgets.Add(new Label { Text = "Slice View", TextColor = Color.White });

        // Axis selector
        var axisRow = new HorizontalStackPanel { Spacing = 4 };
        foreach (var axis in Enum.GetValues<SliceAxis>())
        {
            var btn = new Button
            {
                Content = new Label { Text = axis.ToString() },
                Width = 30,
            };
            var capturedAxis = axis;
            btn.Click += (_, _) =>
            {
                _sliceAxis = capturedAxis;
                _layerIndex = Math.Min(_layerIndex, _state.ActiveModel.GridHint - 1);
            };
            axisRow.Widgets.Add(btn);
        }
        _root.Widgets.Add(axisRow);

        // Layer slider
        var sliderRow = new HorizontalStackPanel { Spacing = 4 };
        var layerLabel = new Label { Text = $"Layer: {_layerIndex}", TextColor = Color.LightGray };
        var slider = new HorizontalSlider
        {
            Minimum = 0,
            Maximum = state.ActiveModel.GridHint - 1,
            Value = _layerIndex,
            Width = 140,
        };
        slider.ValueChanged += (_, _) =>
        {
            _layerIndex = (int)slider.Value;
            layerLabel.Text = $"Layer: {_layerIndex}";
        };
        sliderRow.Widgets.Add(slider);
        sliderRow.Widgets.Add(layerLabel);
        _root.Widgets.Add(sliderRow);

        // Canvas for the grid rendering
        _canvas = new Panel
        {
            Width = CellSize * state.ActiveModel.GridHint,
            Height = CellSize * state.ActiveModel.GridHint,
            Background = new SolidBrush(new Color(20, 20, 25)),
        };
        _root.Widgets.Add(_canvas);
    }

    /// <summary>
    /// Initialize GPU resources. Call once after GraphicsDevice is available.
    /// </summary>
    public void InitializeGraphics(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
        _spriteBatch = new SpriteBatch(graphicsDevice);
        _pixelTexture = new Texture2D(graphicsDevice, 1, 1);
        _pixelTexture.SetData([Color.White]);
    }

    /// <summary>
    /// Draw the slice grid. Call during Draw() after Myra renders (or in a custom render target).
    /// For now this draws to the main backbuffer at a fixed screen position.
    /// </summary>
    public void DrawSlice(int screenX, int screenY)
    {
        if (!Visible || _spriteBatch is null || _pixelTexture is null) return;

        var model = _state.ActiveModel;
        int gridSize = model.GridHint;

        _spriteBatch.Begin();

        for (int v = 0; v < gridSize; v++)
        {
            for (int u = 0; u < gridSize; u++)
            {
                var voxelValue = SliceHelper.GetSliceVoxel(model, _sliceAxis, _layerIndex, u, v);
                Color cellColor;

                if (voxelValue.HasValue)
                {
                    var mat = model.Palette.Get(voxelValue.Value);
                    cellColor = mat is not null
                        ? new Color(mat.Color.R, mat.Color.G, mat.Color.B, mat.Color.A)
                        : Color.Magenta;
                }
                else
                {
                    cellColor = new Color(30, 30, 35);
                }

                _spriteBatch.Draw(_pixelTexture,
                    new Rectangle(screenX + u * CellSize, screenY + v * CellSize, CellSize - 1, CellSize - 1),
                    cellColor);

                // Region label indicator (colored border)
                var worldPos = SliceHelper.SliceToWorld(_sliceAxis, _layerIndex, u, v);
                var region = _state.Labels.GetRegion(worldPos);
                if (region.HasValue)
                {
                    // Draw a thin colored border to indicate labeling
                    _spriteBatch.Draw(_pixelTexture,
                        new Rectangle(screenX + u * CellSize, screenY + v * CellSize, CellSize - 1, 1),
                        Color.Yellow);
                }
            }
        }

        // Grid lines
        for (int i = 0; i <= gridSize; i++)
        {
            // Vertical lines
            _spriteBatch.Draw(_pixelTexture,
                new Rectangle(screenX + i * CellSize, screenY, 1, gridSize * CellSize),
                new Color(50, 50, 55));
            // Horizontal lines
            _spriteBatch.Draw(_pixelTexture,
                new Rectangle(screenX, screenY + i * CellSize, gridSize * CellSize, 1),
                new Color(50, 50, 55));
        }

        _spriteBatch.End();
    }
}
