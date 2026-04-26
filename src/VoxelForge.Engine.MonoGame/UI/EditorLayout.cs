using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Reference;
using VoxelForge.Engine.MonoGame.UI.Panels;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// Arranges all editor panels using Myra Grid layout.
/// Top: menu bar. Left sidebar: Tool + Palette. Right sidebar: Region + Animation + Properties. Bottom: LLM.
/// Center is left empty for the 3D viewport (rendered behind UI).
/// </summary>
public sealed class EditorLayout
{
    public EditorMenuBar MenuBar { get; }
    public ToolPanel ToolPanel { get; }
    public PalettePanel PalettePanel { get; }
    public ContentBrowserPanel ContentBrowser { get; }
    public RegionPanel RegionPanel { get; }
    public AnimationPanel AnimationPanel { get; }
    public PropertiesPanel PropertiesPanel { get; }
    public ReferenceModelPanel? RefModelPanel { get; }
    public MaterialPanel MaterialPanel { get; }
    public LlmPanel LlmPanel { get; }
    public ContentDragDrop DragDrop { get; }
    public VerticalStackPanel RightSidebar { get; private set; } = null!;
    public Widget Root { get; }

    public EditorLayout(EditorState state, MenuCommandDispatcher? dispatcher = null,
        ReferenceModelState? referenceModelState = null)
    {
        DragDrop = new ContentDragDrop();
        ToolPanel = new ToolPanel(state);
        PalettePanel = new PalettePanel(state);
        ContentBrowser = new ContentBrowserPanel(DragDrop);
        RegionPanel = new RegionPanel(state);
        AnimationPanel = new AnimationPanel(state);
        PropertiesPanel = new PropertiesPanel(state);
        LlmPanel = new LlmPanel(state);

        MaterialPanel = new MaterialPanel(state, DragDrop);

        if (dispatcher is not null && referenceModelState is not null)
            RefModelPanel = new ReferenceModelPanel(referenceModelState, dispatcher);

        // Main grid: 2 rows (menu bar + main area)
        var outerGrid = new Grid();
        outerGrid.RowsProportions.Add(new Proportion(ProportionType.Auto));  // menu bar
        outerGrid.RowsProportions.Add(new Proportion(ProportionType.Fill));  // main area

        // Menu bar (row 0)
        if (dispatcher is not null)
        {
            MenuBar = new EditorMenuBar(dispatcher);
            Grid.SetRow(MenuBar.Menu, 0);
            outerGrid.Widgets.Add(MenuBar.Menu);
        }
        else
        {
            MenuBar = null!;
        }

        // Main area: 3 columns (left sidebar, center viewport, right sidebar)
        var mainGrid = new Grid();
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Pixels, 160));
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Fill));
        mainGrid.ColumnsProportions.Add(new Proportion(ProportionType.Pixels, 180));

        // Left sidebar — collapsible sections
        var leftPanel = new VerticalStackPanel { Spacing = 4 };
        leftPanel.Widgets.Add(new CollapsibleSection("Tools", ToolPanel.Root, expanded: true).Root);
        leftPanel.Widgets.Add(new CollapsibleSection("Palette", PalettePanel.Root, expanded: true).Root);
        leftPanel.Widgets.Add(new CollapsibleSection("Content", ContentBrowser.Root, expanded: false).Root);
        if (RefModelPanel is not null)
            leftPanel.Widgets.Add(new CollapsibleSection("Ref Models", RefModelPanel.Root, expanded: true).Root);
        leftPanel.Widgets.Add(new CollapsibleSection("Material", MaterialPanel.Root, expanded: true).Root);
        var leftScroll = new ScrollViewer
        {
            Content = leftPanel,
            ShowHorizontalScrollBar = false,
        };
        Grid.SetColumn(leftScroll, 0);
        mainGrid.Widgets.Add(leftScroll);

        // Center is empty — 3D viewport renders behind the UI

        // Right sidebar — collapsible sections
        RightSidebar = new VerticalStackPanel { Spacing = 4 };
        RightSidebar.Widgets.Add(new CollapsibleSection("Regions", RegionPanel.Root, expanded: false).Root);
        RightSidebar.Widgets.Add(new CollapsibleSection("Animation", AnimationPanel.Root, expanded: false).Root);
        RightSidebar.Widgets.Add(new CollapsibleSection("Properties", PropertiesPanel.Root, expanded: true).Root);
        var rightScroll = new ScrollViewer
        {
            Content = RightSidebar,
            ShowHorizontalScrollBar = false,
        };
        Grid.SetColumn(rightScroll, 2);
        mainGrid.Widgets.Add(rightScroll);

        // LLM panel in the lower-right sidebar
        RightSidebar.Widgets.Add(new CollapsibleSection("LLM Chat", LlmPanel.Root, expanded: false).Root);

        Grid.SetRow(mainGrid, 1);
        outerGrid.Widgets.Add(mainGrid);

        // Drag-drop overlay (spans full window so the label can appear anywhere)
        Grid.SetRowSpan(DragDrop.DragLabel, 2);
        outerGrid.Widgets.Add(DragDrop.DragLabel);

        // Register drop targets
        if (RefModelPanel is not null && dispatcher is not null)
        {
            DragDrop.RegisterTarget(
                RefModelPanel.Root,
                ContentBrowserPanel.IsModelFile,
                path => dispatcher.Dispatch("refload", path));
        }

        Root = outerGrid;
    }

    public void Refresh()
    {
        PropertiesPanel.Refresh();
        RefModelPanel?.Refresh();
        MaterialPanel.Refresh();
    }
}
