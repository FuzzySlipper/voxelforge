using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.Engine.MonoGame.UI.Panels;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Arranges all editor panels using Myra Grid layout.
/// Left sidebar: Tool + Palette. Right sidebar: Region + Animation + Properties. Bottom: LLM.
/// Center is left empty for the 3D viewport (rendered behind UI).
/// </summary>
public sealed class EditorLayout
{
    public ToolPanel ToolPanel { get; }
    public PalettePanel PalettePanel { get; }
    public RegionPanel RegionPanel { get; }
    public AnimationPanel AnimationPanel { get; }
    public PropertiesPanel PropertiesPanel { get; }
    public LlmPanel LlmPanel { get; }
    public VerticalStackPanel RightSidebar { get; private set; } = null!;
    public Widget Root { get; }

    public EditorLayout(EditorState state)
    {
        ToolPanel = new ToolPanel(state);
        PalettePanel = new PalettePanel(state);
        RegionPanel = new RegionPanel(state);
        AnimationPanel = new AnimationPanel(state);
        PropertiesPanel = new PropertiesPanel(state);
        LlmPanel = new LlmPanel(state);

        // Main grid: 2 rows (main area + bottom panel)
        var outerGrid = new Grid();
        outerGrid.RowsProportions.Add(new Proportion(ProportionType.Fill));
        outerGrid.RowsProportions.Add(new Proportion(ProportionType.Pixels, 180));

        // Main area: 3 columns (left sidebar, center viewport, right sidebar)
        var mainGrid = new Grid();
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Pixels, 160));
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Fill));
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Pixels, 180));

        // Left sidebar
        var leftPanel = new VerticalStackPanel { Spacing = 8 };
        leftPanel.Widgets.Add(ToolPanel.Root);
        leftPanel.Widgets.Add(PalettePanel.Root);
        var leftScroll = new ScrollViewer
        {
            Content = leftPanel,
            ShowHorizontalScrollBar = false,
        };
        Grid.SetColumn(leftScroll, 0);
        mainGrid.Widgets.Add(leftScroll);

        // Center is empty — 3D viewport renders behind the UI

        // Right sidebar
        RightSidebar = new VerticalStackPanel { Spacing = 8 };
        RightSidebar.Widgets.Add(RegionPanel.Root);
        RightSidebar.Widgets.Add(AnimationPanel.Root);
        RightSidebar.Widgets.Add(PropertiesPanel.Root);
        var rightPanel = RightSidebar;
        var rightScroll = new ScrollViewer
        {
            Content = rightPanel,
            ShowHorizontalScrollBar = false,
        };
        Grid.SetColumn(rightScroll, 2);
        mainGrid.Widgets.Add(rightScroll);

        Grid.SetRow(mainGrid, 0);
        outerGrid.Widgets.Add(mainGrid);

        // Bottom: LLM panel
        var bottomScroll = new ScrollViewer
        {
            Content = LlmPanel.Root,
            ShowHorizontalScrollBar = false,
        };
        Grid.SetRow(bottomScroll, 1);
        outerGrid.Widgets.Add(bottomScroll);

        Root = outerGrid;
    }

    public void Refresh()
    {
        PropertiesPanel.Refresh();
    }
}
