using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.File;

namespace VoxelForge.Engine.MonoGame.UI;

/// <summary>
/// FileDialog subclass that adds a "Content" shortcut in the places sidebar,
/// pointing to the project's content/ directory.
/// </summary>
public class VoxelForgeFileDialog : FileDialog
{
    private static readonly string ContentPath = Path.GetFullPath("content");

    public VoxelForgeFileDialog(FileDialogMode mode) : base(mode)
    {
    }

    protected override void PopulatePlacesListUI(ListView listView)
    {
        // Add the content directory shortcut first
        if (Directory.Exists(ContentPath))
        {
            var location = new Location(string.Empty, "Content", ContentPath, false);
            listView.Widgets.Add(CreateListItem(location));
            listView.Widgets.Add(new HorizontalSeparator());
        }

        base.PopulatePlacesListUI(listView);
    }
}
