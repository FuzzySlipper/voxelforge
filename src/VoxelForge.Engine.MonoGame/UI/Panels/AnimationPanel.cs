using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class AnimationPanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly HorizontalStackPanel _frameStrip;
    private bool _playing;

    public Widget Root => _root;

    public AnimationPanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "Animation", TextColor = Color.White });

        // Frame strip
        _frameStrip = new HorizontalStackPanel { Spacing = 2 };
        var stripScroll = new ScrollViewer
        {
            Content = _frameStrip,
            ShowVerticalScrollBar = false,
        };
        _root.Widgets.Add(stripScroll);

        // Controls
        var controls = new HorizontalStackPanel { Spacing = 4 };

        var baseBtn = new Button { Content = new Label { Text = "Base" }, Width = 40 };
        baseBtn.Click += (_, _) =>
        {
            _state.ActiveFrameIndex = -1;
            Refresh();
        };
        controls.Widgets.Add(baseBtn);

        var addBtn = new Button { Content = new Label { Text = "+" }, Width = 24 };
        addBtn.Click += (_, _) =>
        {
            if (_state.Clips.Count == 0) return;
            _state.Clips[0].AddFrame();
            Refresh();
        };
        controls.Widgets.Add(addBtn);

        var playBtn = new Button { Content = new Label { Text = "Play" } };
        playBtn.Click += (_, _) => _playing = !_playing;
        controls.Widgets.Add(playBtn);

        _root.Widgets.Add(controls);
        Refresh();
    }

    public void Refresh()
    {
        _frameStrip.Widgets.Clear();

        if (_state.Clips.Count == 0) return;

        var clip = _state.Clips[0];
        for (int i = 0; i < clip.Frames.Count; i++)
        {
            var btn = new Button
            {
                Content = new Label { Text = $"F{i}" },
                Width = 30,
                Background = i == _state.ActiveFrameIndex
                    ? new SolidBrush(new Color(80, 120, 200))
                    : new SolidBrush(new Color(60, 60, 60)),
            };
            int capturedIndex = i;
            btn.Click += (_, _) =>
            {
                _state.ActiveFrameIndex = capturedIndex;
                Refresh();
            };
            _frameStrip.Widgets.Add(btn);
        }
    }

    public bool IsPlaying => _playing;
}
