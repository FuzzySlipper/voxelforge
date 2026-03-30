using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// A clickable header that toggles visibility of child content.
/// Used to wrap sidebar panels so the UI stays clean.
/// </summary>
public sealed class CollapsibleSection
{
    private readonly Label _headerLabel;
    private readonly Widget _content;
    private bool _expanded;

    public Widget Root { get; }
    public string Title { get; }

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            _content.Visible = value;
            UpdateHeaderText();
        }
    }

    public CollapsibleSection(string title, Widget content, bool expanded = true)
    {
        Title = title;
        _content = content;
        _expanded = expanded;

        var panel = new VerticalStackPanel { Spacing = 2 };

        // Clickable header
        var headerBtn = new Button
        {
            Content = _headerLabel = new Label
            {
                Text = FormatHeader(title, expanded),
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidBrush(new Color(50, 50, 55)),
        };

        headerBtn.Click += (_, _) => IsExpanded = !IsExpanded;

        panel.Widgets.Add(headerBtn);
        panel.Widgets.Add(content);
        content.Visible = expanded;

        Root = panel;
    }

    private void UpdateHeaderText()
    {
        _headerLabel.Text = FormatHeader(Title, _expanded);
    }

    private static string FormatHeader(string title, bool expanded)
    {
        return expanded ? $"v {title}" : $"> {title}";
    }
}
