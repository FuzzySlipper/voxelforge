namespace VoxelForge.Mcp.Viewer;

/// <summary>
/// Loads viewer HTML from the wwwroot file at startup.
/// </summary>
public static class ViewerHtml
{
    private static string? _cached;

    /// <summary>
    /// The viewer HTML content. Loaded once from the wwwroot/viewer.html file
    /// located alongside the built assembly.
    /// </summary>
    public static string Content
    {
        get
        {
            if (_cached is null)
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

                _cached = File.ReadAllText(path);
            }

            return _cached;
        }
    }
}
