using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using VoxelForge.App;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class LlmPanel
{
    private readonly VerticalStackPanel _root;
    private readonly Label _historyLabel;
    private readonly TextBox _inputBox;
    private readonly List<string> _messages = [];

    public Widget Root => _root;

    public LlmPanel(EditorState state)
    {
        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "LLM Assistant", TextColor = Color.White });

        _historyLabel = new Label
        {
            Text = "Type a message to get started...",
            TextColor = Color.LightGray,
            Wrap = true,
        };
        var historyScroll = new ScrollViewer
        {
            Content = _historyLabel,
            ShowHorizontalScrollBar = false,
            Height = 120,
        };
        _root.Widgets.Add(historyScroll);

        var inputRow = new HorizontalStackPanel { Spacing = 4 };
        _inputBox = new TextBox { Width = 200 };
        inputRow.Widgets.Add(_inputBox);

        var sendBtn = new Button { Content = new Label { Text = "Send" } };
        sendBtn.Click += (_, _) => SubmitInput();
        inputRow.Widgets.Add(sendBtn);

        _root.Widgets.Add(inputRow);
    }

    private void SubmitInput()
    {
        var text = _inputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _messages.Add($"> {text}");
        _historyLabel.Text = string.Join("\n", _messages);
        _inputBox.Text = "";

    }

    public void AppendResponse(string response)
    {
        _messages.Add(response);
        _historyLabel.Text = string.Join("\n", _messages);
    }
}
