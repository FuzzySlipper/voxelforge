namespace Architecture.Tests;

public sealed class EngineUiStatusTests
{
    [Fact]
    public void EditorStatusEvent_IsConsumedByNamedEngineUiHandler()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var enginePath = Path.Combine(root, "src", "VoxelForge.Engine.MonoGame");
        var eventHandlersText = File.ReadAllText(Path.Combine(enginePath, "EventHandlers.cs"));
        var editorLayoutText = File.ReadAllText(Path.Combine(enginePath, "UI", "EditorLayout.cs"));
        var gameText = File.ReadAllText(Path.Combine(enginePath, "VoxelForgeGame.cs"));

        Assert.Contains("EditorStatusPanelEventHandler : IEventHandler<EditorStatusEvent>", eventHandlersText, StringComparison.Ordinal);
        Assert.Contains("events.Register<EditorStatusEvent>(new EditorStatusPanelEventHandler(StatusPanel));", editorLayoutText, StringComparison.Ordinal);
        Assert.Contains("_editorLayout?.Tick(gameTime.ElapsedGameTime);", gameText, StringComparison.Ordinal);
    }

    private static string FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "voxelforge.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate voxelforge repository root.");
    }
}
