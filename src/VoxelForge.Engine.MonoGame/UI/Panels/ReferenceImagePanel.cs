using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Myra.Graphics2D.TextureAtlases;
using Myra.Graphics2D.UI;
using VoxelForge.App.Reference;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// Displays loaded reference images in tabs with scroll/zoom.
/// </summary>
public sealed class ReferenceImagePanel : IDisposable
{
    private readonly ReferenceImageState _referenceImageState;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly VerticalStackPanel _root;
    private readonly VerticalStackPanel _tabContent;
    private readonly HorizontalStackPanel _tabBar;
    private readonly List<Texture2D> _textures = [];
    private int _activeIndex = -1;

    public Widget Root => _root;

    public ReferenceImagePanel(ReferenceImageState referenceImageState, GraphicsDevice graphicsDevice)
    {
        _referenceImageState = referenceImageState;
        _graphicsDevice = graphicsDevice;

        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "Reference Images", TextColor = Color.White });

        _tabBar = new HorizontalStackPanel { Spacing = 2 };
        _root.Widgets.Add(_tabBar);

        _tabContent = new VerticalStackPanel();
        var scroll = new ScrollViewer
        {
            Content = _tabContent,
            Height = 200,
        };
        _root.Widgets.Add(scroll);

        _referenceImageState.Changed += Rebuild;
    }

    public void Rebuild()
    {
        // Dispose old textures
        foreach (var tex in _textures)
            tex.Dispose();
        _textures.Clear();
        _tabBar.Widgets.Clear();
        _tabContent.Widgets.Clear();

        // Build textures and tabs
        for (int i = 0; i < _referenceImageState.Images.Count; i++)
        {
            var entry = _referenceImageState.Images[i];
            try
            {
                using var ms = new MemoryStream(entry.RawBytes);
                var tex = Texture2D.FromStream(_graphicsDevice, ms);
                _textures.Add(tex);
            }
            catch
            {
                // If image fails to load, add a placeholder
                var placeholder = new Texture2D(_graphicsDevice, 1, 1);
                placeholder.SetData([Color.Magenta]);
                _textures.Add(placeholder);
            }

            var btn = new Button
            {
                Content = new Label { Text = entry.Label.Length > 10 ? entry.Label[..10] : entry.Label },
                Width = 80,
            };
            int capturedIndex = i;
            btn.Click += (_, _) => ShowImage(capturedIndex);
            _tabBar.Widgets.Add(btn);
        }

        if (_referenceImageState.Images.Count > 0)
            ShowImage(0);
        else
            _activeIndex = -1;
    }

    private void ShowImage(int index)
    {
        if (index < 0 || index >= _textures.Count) return;
        _activeIndex = index;

        _tabContent.Widgets.Clear();

        var tex = _textures[index];
        var img = new Image
        {
            Renderable = new TextureRegion(tex),
            Width = tex.Width,
            Height = tex.Height,
        };
        _tabContent.Widgets.Add(img);
    }

    public void Dispose()
    {
        foreach (var tex in _textures)
            tex.Dispose();
    }
}
