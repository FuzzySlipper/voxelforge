using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// Edits the material at the active palette index: name, color, and texture slots.
/// Texture slots accept drag-and-drop from the content browser. Textures are stored
/// as file paths in MaterialDef.Metadata (keys: tex_albedo, tex_normal, tex_roughness, tex_emissive).
/// </summary>
public sealed class MaterialPanel
{
    private record struct TextureSlot(string MetadataKey, string DisplayName, Label FileLabel);

    private static readonly SolidBrush SlotBackground = new(new Color(50, 50, 55));
    private static readonly Color SlotTextColor = new(160, 160, 160);

    private readonly EditorState _state;
    private readonly IEventPublisher _events;
    private readonly VerticalStackPanel _root;
    private readonly VerticalStackPanel _propsSection;
    private readonly Label _headerLabel;
    private readonly Label _noSelectionLabel;
    private readonly TextBox _nameField;
    private readonly Panel _colorSwatch;
    private readonly TextBox _rField, _gField, _bField;
    private readonly List<TextureSlot> _slots = [];

    private byte _displayedIndex;
    private bool _updating;

    public Widget Root => _root;

    public MaterialPanel(EditorState state, ContentDragDrop dragDrop, IEventPublisher events)
    {
        _state = state;
        _events = events;

        _root = new VerticalStackPanel { Spacing = 4 };

        _headerLabel = new Label { Text = "Material", TextColor = Color.White };
        _root.Widgets.Add(_headerLabel);

        _noSelectionLabel = new Label { Text = "No material selected", TextColor = new Color(120, 120, 120) };
        _root.Widgets.Add(_noSelectionLabel);

        _propsSection = new VerticalStackPanel { Spacing = 4, Visible = false };

        // -- Name
        var nameRow = new HorizontalStackPanel { Spacing = 4 };
        nameRow.Widgets.Add(new Label { Text = "Name", Width = 38 });
        _nameField = new TextBox { Width = 105 };
        _nameField.TextChanged += (_, _) => OnNameChanged();
        nameRow.Widgets.Add(_nameField);
        _propsSection.Widgets.Add(nameRow);

        // -- Color
        var colorRow = new HorizontalStackPanel { Spacing = 2 };
        _colorSwatch = new Panel
        {
            Width = 20,
            Height = 20,
            Background = new SolidBrush(Color.White),
            BorderThickness = new Thickness(1),
            Border = new SolidBrush(new Color(80, 80, 80)),
        };
        colorRow.Widgets.Add(_colorSwatch);
        _rField = SmallField();
        _gField = SmallField();
        _bField = SmallField();
        _rField.TextChanged += (_, _) => OnColorChanged();
        _gField.TextChanged += (_, _) => OnColorChanged();
        _bField.TextChanged += (_, _) => OnColorChanged();
        colorRow.Widgets.Add(new Label { Text = "R", Width = 10 });
        colorRow.Widgets.Add(_rField);
        colorRow.Widgets.Add(new Label { Text = "G", Width = 10 });
        colorRow.Widgets.Add(_gField);
        colorRow.Widgets.Add(new Label { Text = "B", Width = 10 });
        colorRow.Widgets.Add(_bField);
        _propsSection.Widgets.Add(colorRow);

        // -- Texture slots
        _propsSection.Widgets.Add(new Label { Text = "Textures", TextColor = new Color(180, 180, 180) });
        AddTextureSlot(dragDrop, "tex_albedo", "Albedo");
        AddTextureSlot(dragDrop, "tex_normal", "Normal");
        AddTextureSlot(dragDrop, "tex_roughness", "Roughness");
        AddTextureSlot(dragDrop, "tex_emissive", "Emissive");

        _root.Widgets.Add(_propsSection);
    }

    private void AddTextureSlot(ContentDragDrop dragDrop, string metadataKey, string displayName)
    {
        var row = new HorizontalStackPanel { Spacing = 2 };
        row.Background = SlotBackground;
        row.Padding = new Thickness(2);

        var fileLabel = new Label
        {
            Text = $"{displayName}: None",
            TextColor = SlotTextColor,
            Width = 118,
        };
        row.Widgets.Add(fileLabel);

        var clearBtn = new Button { Content = new Label { Text = "X" }, Width = 22, Height = 22 };
        var key = metadataKey;
        clearBtn.Click += (_, _) => ClearTexture(key);
        row.Widgets.Add(clearBtn);

        _propsSection.Widgets.Add(row);
        _slots.Add(new TextureSlot(metadataKey, displayName, fileLabel));

        dragDrop.RegisterTarget(
            row,
            ContentBrowserPanel.IsImageFile,
            path => SetTexture(key, path));
    }

    /// <summary>
    /// Call each frame to sync the panel with the active palette selection.
    /// </summary>
    public void Refresh()
    {
        var index = _state.ActivePaletteIndex;
        var mat = _state.ActiveModel.Palette.Get(index);

        if (mat == null)
        {
            _propsSection.Visible = false;
            _noSelectionLabel.Visible = true;
            _headerLabel.Text = "Material";
            return;
        }

        _noSelectionLabel.Visible = false;
        _propsSection.Visible = true;
        _headerLabel.Text = $"Material [{index}]";

        if (index == _displayedIndex) return;
        _displayedIndex = index;

        _updating = true;

        _nameField.Text = mat.Name;
        _rField.Text = mat.Color.R.ToString();
        _gField.Text = mat.Color.G.ToString();
        _bField.Text = mat.Color.B.ToString();
        _colorSwatch.Background = new SolidBrush(
            new Color(mat.Color.R, mat.Color.G, mat.Color.B, mat.Color.A));

        foreach (var slot in _slots)
        {
            if (mat.Metadata.TryGetValue(slot.MetadataKey, out var path) && !string.IsNullOrEmpty(path))
                slot.FileLabel.Text = $"{slot.DisplayName}: {Path.GetFileName(path)}";
            else
                slot.FileLabel.Text = $"{slot.DisplayName}: None";
        }

        _updating = false;
    }

    private void OnNameChanged()
    {
        if (_updating) return;
        var mat = GetCurrentMaterial();
        if (mat == null) return;

        _state.ActiveModel.Palette.Set(_state.ActivePaletteIndex, new MaterialDef
        {
            Name = _nameField.Text ?? mat.Name,
            Color = mat.Color,
            Metadata = new Dictionary<string, string>(mat.Metadata),
        });
        PublishPaletteChanged(PaletteChangeKind.EntryUpdated, "Material name changed");
    }

    private void OnColorChanged()
    {
        if (_updating) return;
        var mat = GetCurrentMaterial();
        if (mat == null) return;

        if (!byte.TryParse(_rField.Text, out byte r) ||
            !byte.TryParse(_gField.Text, out byte g) ||
            !byte.TryParse(_bField.Text, out byte b))
            return;

        _colorSwatch.Background = new SolidBrush(new Color(r, g, b));

        _state.ActiveModel.Palette.Set(_state.ActivePaletteIndex, new MaterialDef
        {
            Name = mat.Name,
            Color = new RgbaColor(r, g, b, mat.Color.A),
            Metadata = new Dictionary<string, string>(mat.Metadata),
        });
        PublishPaletteChanged(PaletteChangeKind.EntryUpdated, "Material color changed");
    }

    private void SetTexture(string metadataKey, string filePath)
    {
        var mat = GetCurrentMaterial();
        if (mat == null) return;

        var meta = new Dictionary<string, string>(mat.Metadata) { [metadataKey] = filePath };
        _state.ActiveModel.Palette.Set(_state.ActivePaletteIndex, new MaterialDef
        {
            Name = mat.Name,
            Color = mat.Color,
            Metadata = meta,
        });

        foreach (var slot in _slots)
        {
            if (slot.MetadataKey == metadataKey)
            {
                slot.FileLabel.Text = $"{slot.DisplayName}: {Path.GetFileName(filePath)}";
                break;
            }
        }

        PublishPaletteChanged(PaletteChangeKind.TextureChanged, $"Material texture {metadataKey} set");
    }

    private void ClearTexture(string metadataKey)
    {
        var mat = GetCurrentMaterial();
        if (mat == null) return;

        var meta = new Dictionary<string, string>(mat.Metadata);
        meta.Remove(metadataKey);
        _state.ActiveModel.Palette.Set(_state.ActivePaletteIndex, new MaterialDef
        {
            Name = mat.Name,
            Color = mat.Color,
            Metadata = meta,
        });

        foreach (var slot in _slots)
        {
            if (slot.MetadataKey == metadataKey)
            {
                slot.FileLabel.Text = $"{slot.DisplayName}: None";
                break;
            }
        }

        PublishPaletteChanged(PaletteChangeKind.TextureChanged, $"Material texture {metadataKey} cleared");
    }

    private void PublishPaletteChanged(PaletteChangeKind kind, string description)
    {
        _events.Publish(new PaletteChangedEvent(kind, description, _state.ActivePaletteIndex, 1));
    }

    private MaterialDef? GetCurrentMaterial()
        => _state.ActiveModel.Palette.Get(_state.ActivePaletteIndex);

    private static TextBox SmallField() => new() { Width = 30 };
}
