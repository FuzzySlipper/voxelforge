using Microsoft.Xna.Framework;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.Core;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

public sealed class RegionPanel
{
    private readonly EditorState _state;
    private readonly VerticalStackPanel _root;
    private readonly VerticalStackPanel _regionList;

    public Widget Root => _root;

    public RegionPanel(EditorState state)
    {
        _state = state;
        _root = new VerticalStackPanel { Spacing = 4 };
        _root.Widgets.Add(new Label { Text = "Regions", TextColor = Color.White });

        _regionList = new VerticalStackPanel { Spacing = 2 };
        var scroll = new ScrollViewer
        {
            Content = _regionList,
            ShowHorizontalScrollBar = false,
        };
        _root.Widgets.Add(scroll);

        var addButton = new Button { Content = new Label { Text = "+ Add Region" }, Width = 140 };
        addButton.Click += (_, _) =>
        {
            var name = $"region_{_state.Labels.Regions.Count}";
            var id = new RegionId(name);
            _state.Labels.AddOrUpdateRegion(new RegionDef { Id = id, Name = name });
            Refresh();
        };
        _root.Widgets.Add(addButton);

        Refresh();
    }

    public void Refresh()
    {
        _regionList.Widgets.Clear();

        foreach (var (id, def) in _state.Labels.Regions)
        {
            var label = new Label
            {
                Text = $"{def.Name} ({def.Voxels.Count})",
                TextColor = _state.ActiveRegion == id ? Color.Yellow : Color.LightGray,
            };

            var capturedId = id;
            label.TouchDown += (_, _) =>
            {
                _state.ActiveRegion = capturedId;
                Refresh();
            };

            _regionList.Widgets.Add(label);
        }
    }
}
