using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App.Events;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// Bottom status message area for typed App-layer editor status events.
/// </summary>
public sealed class EditorStatusPanel
{
    private static readonly TimeSpan DefaultStatusLifetime = TimeSpan.FromSeconds(6);
    private readonly Label _statusLabel;
    private TimeSpan _remainingStatusLifetime;

    public Widget Root { get; }
    public string CurrentText => _statusLabel.Text ?? string.Empty;
    public EditorStatusSeverity? CurrentSeverity { get; private set; }

    public EditorStatusPanel()
    {
        var root = new HorizontalStackPanel
        {
            Spacing = 6,
            Padding = new Thickness(6, 2),
            Background = new SolidBrush(new Color(35, 35, 40, 230)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        root.Widgets.Add(new Label
        {
            Text = "Status",
            TextColor = Color.White,
        });

        _statusLabel = new Label
        {
            Text = "Ready",
            TextColor = Color.LightGray,
            Wrap = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Widgets.Add(_statusLabel);

        Root = root;
    }

    public void ShowStatus(EditorStatusEvent statusEvent)
    {
        ArgumentNullException.ThrowIfNull(statusEvent);

        CurrentSeverity = statusEvent.Severity;
        _remainingStatusLifetime = DefaultStatusLifetime;
        _statusLabel.TextColor = GetStatusColor(statusEvent.Severity);
        _statusLabel.Text = FormatStatus(statusEvent);
    }

    public void Tick(TimeSpan elapsed)
    {
        if (_remainingStatusLifetime <= TimeSpan.Zero)
            return;

        _remainingStatusLifetime -= elapsed;
        if (_remainingStatusLifetime <= TimeSpan.Zero)
            ClearStatus();
    }

    public void ClearStatus()
    {
        _remainingStatusLifetime = TimeSpan.Zero;
        CurrentSeverity = null;
        _statusLabel.TextColor = Color.LightGray;
        _statusLabel.Text = "Ready";
    }

    private static string FormatStatus(EditorStatusEvent statusEvent)
    {
        return $"{statusEvent.Severity}: {statusEvent.Source} — {statusEvent.Message}";
    }

    private static Color GetStatusColor(EditorStatusSeverity severity)
    {
        return severity switch
        {
            EditorStatusSeverity.Info => Color.LightGray,
            EditorStatusSeverity.Warning => Color.Yellow,
            EditorStatusSeverity.Error => Color.OrangeRed,
            _ => Color.LightGray,
        };
    }
}
