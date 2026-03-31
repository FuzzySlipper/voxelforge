using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Manages drag-and-drop state between the content browser tree and drop target panels.
/// Drag tracking is driven by the game loop (raw mouse state) rather than Myra touch
/// events, since Myra lacks cross-widget touch capture.
/// </summary>
public sealed class ContentDragDrop
{
    private readonly record struct Target(Widget Widget, Func<string, bool> Accept, Action<string> OnDrop);

    private readonly List<Target> _targets = [];

    private string? _draggedPath;
    private Widget? _highlighted;
    private IBrush? _highlightOrigBorder;
    private Thickness _highlightOrigThickness;

    /// <summary>
    /// Floating label shown at the cursor during an active drag.
    /// Add this to the root layout grid spanning all rows so it can appear anywhere.
    /// </summary>
    public Label DragLabel { get; } = new()
    {
        TextColor = Color.White,
        Background = new SolidBrush(new Color(60, 60, 65, 220)),
        Padding = new Thickness(6, 2),
        Visible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    public string? DraggedPath => _draggedPath;
    public bool IsDragging => _draggedPath != null;

    /// <summary>
    /// Register a widget as a valid drop target.
    /// </summary>
    public void RegisterTarget(Widget widget, Func<string, bool> accept, Action<string> onDrop)
    {
        _targets.Add(new Target(widget, accept, onDrop));
    }

    /// <summary>
    /// Start an active drag. Called by the game loop after the movement threshold is exceeded.
    /// </summary>
    public void BeginDrag(string filePath)
    {
        _draggedPath = filePath;
        DragLabel.Text = Path.GetFileName(filePath);
        DragLabel.Visible = true;
    }

    /// <summary>
    /// Update drag label position and highlight any valid drop target under the cursor.
    /// </summary>
    public void UpdateDrag(Point mousePos)
    {
        if (_draggedPath == null) return;

        DragLabel.Left = mousePos.X + 14;
        DragLabel.Top = mousePos.Y + 2;

        UpdateHighlight(mousePos);
    }

    /// <summary>
    /// End the drag. If the cursor is over a valid drop target, invokes its callback.
    /// Returns true if a drop was handled.
    /// </summary>
    public bool EndDrag(Point mousePos)
    {
        bool handled = false;

        if (_draggedPath != null)
        {
            foreach (var target in _targets)
            {
                if (target.Accept(_draggedPath) && target.Widget.ContainsGlobalPoint(mousePos))
                {
                    target.OnDrop(_draggedPath);
                    handled = true;
                    break;
                }
            }
        }

        Reset();
        return handled;
    }

    public void CancelDrag() => Reset();

    private void Reset()
    {
        ClearHighlight();
        DragLabel.Visible = false;
        _draggedPath = null;
    }

    private void UpdateHighlight(Point mousePos)
    {
        Widget? hit = null;
        foreach (var target in _targets)
        {
            if (target.Accept(_draggedPath!) && target.Widget.ContainsGlobalPoint(mousePos))
            {
                hit = target.Widget;
                break;
            }
        }

        if (hit == _highlighted) return;

        ClearHighlight();

        if (hit != null)
        {
            _highlighted = hit;
            _highlightOrigBorder = hit.Border;
            _highlightOrigThickness = hit.BorderThickness;
            hit.Border = new SolidBrush(new Color(80, 200, 80));
            hit.BorderThickness = new Thickness(2);
        }
    }

    private void ClearHighlight()
    {
        if (_highlighted != null)
        {
            _highlighted.Border = _highlightOrigBorder;
            _highlighted.BorderThickness = _highlightOrigThickness;
            _highlighted = null;
        }
    }
}
