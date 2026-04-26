using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;
using VoxelForge.Core.Serialization;

namespace VoxelForge.App.Console.Commands;

public sealed class SaveCommand : IConsoleCommand
{
    private const string ContentDir = "content";
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "save";
    public string[] Aliases => [];
    public string HelpText => "Save project. Usage: save <name> (saves to content/<name>.vforge)";

    public SaveCommand(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: save <name>");

        var path = ResolvePath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var serializer = new ProjectSerializer(_loggerFactory);
        var meta = new ProjectMetadata { Name = Path.GetFileNameWithoutExtension(path) };
        var json = serializer.Serialize(context.Model, context.Labels, context.Clips, meta);
        File.WriteAllText(path, json);
        context.Events.Publish(new ProjectSavedEvent(path, json.Length));

        return CommandResult.Ok($"Saved to {path} ({json.Length} bytes)");
    }

    internal static string ResolvePath(string input)
    {
        // If already a rooted path or contains directory separators, use as-is
        if (Path.IsPathRooted(input) || input.Contains(Path.DirectorySeparatorChar) || input.Contains('/'))
            return input;

        // Add .vforge extension if missing
        if (!Path.HasExtension(input))
            input += ".vforge";

        return Path.Combine(ContentDir, input);
    }
}

public sealed class LoadCommand : IConsoleCommand
{
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "load";
    public string[] Aliases => [];
    public string HelpText => "Load project. Usage: load <name> (loads from content/<name>.vforge)";

    public LoadCommand(ILoggerFactory loggerFactory) => _loggerFactory = loggerFactory;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: load <name>");

        var path = SaveCommand.ResolvePath(args[0]);

        if (!File.Exists(path))
            return CommandResult.Fail($"File not found: {path}");

        var json = File.ReadAllText(path);
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

        int voxelCount = model.GetVoxelCount();
        int regionCount = labels.Regions.Count;
        int clipCount = clips.Count;
        context.Events.Publish(new ProjectLoadedEvent(path, meta.Name, voxelCount, regionCount, clipCount));
        context.Events.Publish(new VoxelModelChangedEvent(
            VoxelModelChangeKind.ProjectLoaded,
            $"Loaded project '{meta.Name}'",
            voxelCount));
        context.Events.Publish(new LabelChangedEvent(
            LabelChangeKind.LabelsRebuilt,
            $"Loaded {regionCount} region(s) from project",
            null,
            0));
        context.Events.Publish(new PaletteChangedEvent(
            PaletteChangeKind.EntriesChanged,
            "Loaded project palette",
            null,
            model.Palette.Count));

        return CommandResult.Ok($"Loaded '{meta.Name}' — {voxelCount} voxels, {regionCount} regions, {clipCount} clips");
    }
}
