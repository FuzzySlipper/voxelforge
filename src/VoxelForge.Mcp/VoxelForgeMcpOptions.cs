namespace VoxelForge.Mcp;

public sealed class VoxelForgeMcpOptions
{
    public string ListenUrl { get; set; } = "http://localhost:5201";
    public string ProjectDirectory { get; set; } = "content";

    public string GetResolvedProjectDirectory()
    {
        if (Path.IsPathRooted(ProjectDirectory))
            return ProjectDirectory;

        var repoRoot = FindRepoRoot(Directory.GetCurrentDirectory())
            ?? FindRepoRoot(AppContext.BaseDirectory);
        var baseDirectory = repoRoot ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(baseDirectory, ProjectDirectory));
    }

    private static string? FindRepoRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "voxelforge.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
