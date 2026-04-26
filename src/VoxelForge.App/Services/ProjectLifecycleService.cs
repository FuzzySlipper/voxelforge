using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;
using VoxelForge.Core.Serialization;

namespace VoxelForge.App.Services;

public readonly record struct SaveProjectRequest(string NameOrPath);

public readonly record struct LoadProjectRequest(string NameOrPath);

/// <summary>
/// Stateless service for project save/load orchestration.
/// </summary>
public sealed class ProjectLifecycleService
{
    private const string ContentDir = "content";
    private readonly ILoggerFactory _loggerFactory;

    public ProjectLifecycleService(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public ApplicationServiceResult Save(
        EditorDocumentState document,
        IEventPublisher events,
        SaveProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.NameOrPath);

        var path = ResolvePath(request.NameOrPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var serializer = new ProjectSerializer(_loggerFactory);
        var meta = new ProjectMetadata { Name = Path.GetFileNameWithoutExtension(path) };
        var json = serializer.Serialize(document.Model, document.Labels, document.Clips, meta);
        File.WriteAllText(path, json);

        var applicationEvents = new IApplicationEvent[]
        {
            new ProjectSavedEvent(path, json.Length),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Saved to {path} ({json.Length} bytes)",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult Load(
        EditorDocumentState document,
        IEventPublisher events,
        LoadProjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.NameOrPath);

        var path = ResolvePath(request.NameOrPath);
        if (!File.Exists(path))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"File not found: {path}",
            };
        }

        var json = File.ReadAllText(path);
        var serializer = new ProjectSerializer(_loggerFactory);
        var (model, labels, clips, meta) = serializer.Deserialize(json);

        foreach (var position in document.Model.Voxels.Keys.ToList())
            document.Model.RemoveVoxel(position);

        foreach (var entry in model.Palette.Entries)
            document.Model.Palette.Set(entry.Key, entry.Value);

        foreach (var entry in model.Voxels)
            document.Model.SetVoxel(entry.Key, entry.Value);

        document.Model.GridHint = model.GridHint;
        document.Labels.Rebuild(labels.Regions.Values);
        document.Clips.Clear();
        document.Clips.AddRange(clips);

        int voxelCount = model.GetVoxelCount();
        int regionCount = labels.Regions.Count;
        int clipCount = clips.Count;
        var applicationEvents = new IApplicationEvent[]
        {
            new ProjectLoadedEvent(path, meta.Name, voxelCount, regionCount, clipCount),
            new VoxelModelChangedEvent(
                VoxelModelChangeKind.ProjectLoaded,
                $"Loaded project '{meta.Name}'",
                voxelCount),
            new LabelChangedEvent(
                LabelChangeKind.LabelsRebuilt,
                $"Loaded {regionCount} region(s) from project",
                null,
                0),
            new PaletteChangedEvent(
                PaletteChangeKind.EntriesChanged,
                "Loaded project palette",
                null,
                model.Palette.Count),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Loaded '{meta.Name}' — {voxelCount} voxels, {regionCount} regions, {clipCount} clips",
            Events = applicationEvents,
        };
    }

    internal static string ResolvePath(string input)
    {
        if (Path.IsPathRooted(input) || input.Contains(Path.DirectorySeparatorChar) || input.Contains('/'))
            return input;

        if (!Path.HasExtension(input))
            input += ".vforge";

        return Path.Combine(ContentDir, input);
    }
}
