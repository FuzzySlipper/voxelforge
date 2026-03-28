namespace VoxelForge.App.Console.Commands;

public sealed class ListFilesCommand : IConsoleCommand
{
    private const string ContentDir = "content";

    public string Name => "list";
    public string[] Aliases => ["ls", "files"];
    public string HelpText => "List saved project files in the content directory.";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (!Directory.Exists(ContentDir))
            return CommandResult.Ok("No content directory found.");

        var files = Directory.GetFiles(ContentDir, "*.vforge", SearchOption.TopDirectoryOnly);

        if (files.Length == 0)
            return CommandResult.Ok("No .vforge files in content/.");

        var lines = new List<string>();
        foreach (var file in files.OrderBy(f => f))
        {
            var info = new FileInfo(file);
            var name = Path.GetFileNameWithoutExtension(file);
            var size = info.Length < 1024 ? $"{info.Length} B" : $"{info.Length / 1024} KB";
            lines.Add($"  {name,-30} {size,8}  {info.LastWriteTime:yyyy-MM-dd HH:mm}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}
