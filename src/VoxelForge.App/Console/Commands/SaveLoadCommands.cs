using Microsoft.Extensions.Logging;
using VoxelForge.Core.Serialization;

namespace VoxelForge.App.Console.Commands;

public sealed class SaveCommand : IConsoleCommand
{
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "save";
    public string[] Aliases => [];
    public string HelpText => "Save project. Usage: save <filepath>";

    public SaveCommand(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: save <filepath>");

        var serializer = new ProjectSerializer(_loggerFactory);
        var meta = new ProjectMetadata { Name = Path.GetFileNameWithoutExtension(args[0]) };
        var json = serializer.Serialize(context.Model, context.Labels, context.Clips, meta);
        File.WriteAllText(args[0], json);

        return CommandResult.Ok($"Saved to {args[0]} ({json.Length} bytes)");
    }
}

public sealed class LoadCommand : IConsoleCommand
{
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "load";
    public string[] Aliases => [];
    public string HelpText => "Load project. Usage: load <filepath>";

    public LoadCommand(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: load <filepath>");

        if (!File.Exists(args[0]))
            return CommandResult.Fail($"File not found: {args[0]}");

        var json = File.ReadAllText(args[0]);
        var serializer = new ProjectSerializer(_loggerFactory);
        var (model, labels, clips, meta) = serializer.Deserialize(json);

        // Copy loaded data into the context's model
        // Clear existing voxels
        foreach (var pos in context.Model.Voxels.Keys.ToList())
            context.Model.RemoveVoxel(pos);

        // Copy palette
        foreach (var (idx, mat) in model.Palette.Entries)
            context.Model.Palette.Set(idx, mat);

        // Copy voxels
        foreach (var (pos, val) in model.Voxels)
            context.Model.SetVoxel(pos, val);

        context.Model.GridHint = model.GridHint;

        // Rebuild labels
        context.Labels.Rebuild(labels.Regions.Values);

        // Replace clips
        context.Clips.Clear();
        context.Clips.AddRange(clips);

        context.OnModelChanged?.Invoke();

        return CommandResult.Ok($"Loaded '{meta.Name}' — {model.GetVoxelCount()} voxels, {labels.Regions.Count} regions, {clips.Count} clips");
    }
}
