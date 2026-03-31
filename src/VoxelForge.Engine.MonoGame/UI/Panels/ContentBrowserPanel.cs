using Microsoft.Xna.Framework;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace VoxelForge.Engine.MonoGame.UI.Panels;

/// <summary>
/// Tree view of the content/ directory. Files can be dragged from the tree
/// onto registered drop targets (e.g. the reference model panel) to load them.
/// Drag tracking is driven by the game loop via raw mouse state rather than
/// Myra touch events, since Myra has no touch capture across widgets.
/// </summary>
public sealed class ContentBrowserPanel
{
    private static readonly HashSet<string> ModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".obj", ".gltf", ".glb", ".dae", ".3ds", ".blend"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tga"
    };

    private readonly string _contentRoot;
    private readonly TreeView _treeView;
    private readonly Dictionary<TreeViewNode, string> _filePaths = [];
    private readonly VerticalStackPanel _root;

    public Widget Root => _root;

    public static bool IsModelFile(string path)
        => ModelExtensions.Contains(Path.GetExtension(path));

    public static bool IsImageFile(string path)
        => ImageExtensions.Contains(Path.GetExtension(path));

    public ContentBrowserPanel(ContentDragDrop dragDrop)
    {
        _contentRoot = Path.GetFullPath("content");

        _root = new VerticalStackPanel { Spacing = 4 };

        var header = new HorizontalStackPanel { Spacing = 4 };
        header.Widgets.Add(new Label { Text = "Content", TextColor = Color.White });
        var refreshBtn = new Button
        {
            Content = new Label { Text = "Refresh" },
        };
        refreshBtn.Click += (_, _) => RebuildTree();
        header.Widgets.Add(refreshBtn);
        _root.Widgets.Add(header);

        _treeView = new TreeView
        {
            SelectionBackground = new SolidBrush(new Color(60, 100, 160)),
            SelectionHoverBackground = new SolidBrush(new Color(50, 80, 130)),
        };

        var scroll = new ScrollViewer
        {
            Content = _treeView,
            ShowHorizontalScrollBar = false,
            MaxHeight = 300,
        };
        _root.Widgets.Add(scroll);

        RebuildTree();
    }

    /// <summary>
    /// Returns the full file path of the currently selected tree node,
    /// or null if the selection is a directory or nothing is selected.
    /// </summary>
    public string? GetSelectedFilePath()
    {
        if (_treeView.SelectedNode != null && _filePaths.TryGetValue(_treeView.SelectedNode, out var path))
            return path;
        return null;
    }

    /// <summary>
    /// Returns true if the given global point is within the tree view bounds.
    /// Used by the game loop to determine if a click originated from the tree.
    /// </summary>
    public bool IsPointOverTree(Point globalPos)
        => _treeView.ContainsGlobalPoint(globalPos);

    private void RebuildTree()
    {
        _treeView.RemoveAllSubNodes();
        _filePaths.Clear();

        if (!Directory.Exists(_contentRoot)) return;

        PopulateDirectory(_treeView, _contentRoot, 0);
    }

    private void PopulateDirectory(ITreeViewNode parent, string dirPath, int depth)
    {
        if (depth > 5) return;

        try
        {
            foreach (var dir in Directory.GetDirectories(dirPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(dir);
                var label = new Label { Text = name, TextColor = new Color(180, 200, 220) };
                var node = parent.AddSubNode(label);
                PopulateDirectory(node, dir, depth + 1);
            }

            foreach (var file in Directory.GetFiles(dirPath).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file);
                var ext = Path.GetExtension(file);
                var label = new Label { Text = name, TextColor = GetFileColor(ext) };
                var node = parent.AddSubNode(label);
                _filePaths[node] = file;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static Color GetFileColor(string ext)
    {
        if (ModelExtensions.Contains(ext)) return new Color(220, 190, 110);
        if (ImageExtensions.Contains(ext)) return new Color(110, 200, 150);
        return new Color(180, 180, 180);
    }
}
