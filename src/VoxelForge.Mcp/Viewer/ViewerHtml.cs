namespace VoxelForge.Mcp.Viewer;

/// <summary>
/// Loads viewer HTML from the wwwroot file on first access.
/// </summary>
public static class ViewerHtml
{
    private static readonly Lazy<string> ContentLoader = new(LoadContent, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// The viewer HTML content. Loaded once from the wwwroot/viewer.html file
    /// located alongside the built assembly.
    /// </summary>
    public static string Content => ContentLoader.Value;

    private static string LoadContent()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "wwwroot", "viewer.html");
        if (!File.Exists(path))
        {
            // Fallback: search parent directories (debug/dev layout)
            path = Path.Combine(
                Path.GetDirectoryName(baseDir) ?? baseDir,
                "..", "..", "..", "wwwroot", "viewer.html");
            path = Path.GetFullPath(path);
        }

        if (!File.Exists(path))
        {
            // Last resort: relative to the project root
            path = Path.GetFullPath(
                Path.Combine(baseDir, "..", "..", "..", "..", "..",
                    "src", "VoxelForge.Mcp", "wwwroot", "viewer.html"));
        }

        return File.ReadAllText(path);
    }
}
