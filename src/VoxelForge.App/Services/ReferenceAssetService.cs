using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.Content;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Services;

public readonly record struct LoadReferenceAssetRequest(string FilePath);

public readonly record struct RemoveReferenceAssetRequest(int Position);

public readonly record struct ReferenceModelListEntry(
    int Position,
    string FileName,
    string Format,
    int TotalVertices,
    ReferenceRenderMode RenderMode,
    bool IsVisible);

public readonly record struct ReferenceImageListEntry(int Position, string Label, int ByteCount);

/// <summary>
/// Stateless service for simple reference model and image asset operations.
/// </summary>
public sealed class ReferenceAssetService
{
    private readonly ReferenceModelLoader _referenceModelLoader;

    public ReferenceAssetService(ReferenceModelLoader referenceModelLoader)
    {
        _referenceModelLoader = referenceModelLoader;
    }

    public ApplicationServiceResult LoadModel(
        ReferenceModelState referenceModels,
        IEventPublisher events,
        LoadReferenceAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(referenceModels);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.FilePath);

        try
        {
            var model = _referenceModelLoader.Load(request.FilePath);
            referenceModels.Add(model);
            int position = referenceModels.Models.Count - 1;
            var applicationEvents = new IApplicationEvent[]
            {
                new ReferenceModelChangedEvent(
                    ReferenceModelChangeKind.Loaded,
                    $"Loaded reference model {Path.GetFileName(model.FilePath)}",
                    position),
            };
            events.PublishAll(applicationEvents);

            return new ApplicationServiceResult
            {
                Success = true,
                Message = $"Loaded [{position}] {model.Format} — {model.Meshes.Count} meshes, {model.TotalVertices} vertices, {model.TotalTriangles} triangles",
                Events = applicationEvents,
            };
        }
        catch (Exception ex)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Failed to load: {ex.Message}",
            };
        }
    }

    public ApplicationServiceResult<IReadOnlyList<ReferenceModelListEntry>> ListModels(ReferenceModelState referenceModels)
    {
        ArgumentNullException.ThrowIfNull(referenceModels);

        var entries = new List<ReferenceModelListEntry>();
        for (int i = 0; i < referenceModels.Models.Count; i++)
        {
            var model = referenceModels.Models[i];
            entries.Add(new ReferenceModelListEntry(
                i,
                Path.GetFileName(model.FilePath),
                model.Format,
                model.TotalVertices,
                model.RenderMode,
                model.IsVisible));
        }

        return new ApplicationServiceResult<IReadOnlyList<ReferenceModelListEntry>>
        {
            Success = true,
            Message = entries.Count == 0 ? "No reference models loaded." : "Reference models.",
            Data = entries,
        };
    }

    public ApplicationServiceResult RemoveModel(
        ReferenceModelState referenceModels,
        IEventPublisher events,
        RemoveReferenceAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(referenceModels);
        ArgumentNullException.ThrowIfNull(events);

        if (referenceModels.Get(request.Position) is null)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"No reference model at index {request.Position}.",
            };
        }

        referenceModels.RemoveAt(request.Position);
        var applicationEvents = new IApplicationEvent[]
        {
            new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.Removed,
                $"Removed reference model [{request.Position}]",
                request.Position),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Removed reference model [{request.Position}].",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult ClearModels(ReferenceModelState referenceModels, IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(referenceModels);
        ArgumentNullException.ThrowIfNull(events);

        int count = referenceModels.Models.Count;
        if (count == 0)
        {
            return new ApplicationServiceResult
            {
                Success = true,
                Message = "No reference models to remove.",
            };
        }

        referenceModels.Clear();
        var applicationEvents = new IApplicationEvent[]
        {
            new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.Cleared,
                $"Removed {count} reference model(s)",
                null),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Removed {count} reference model(s).",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult LoadImage(
        ReferenceImageState referenceImages,
        IEventPublisher events,
        LoadReferenceAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(referenceImages);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.FilePath);

        if (!File.Exists(request.FilePath))
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"File not found: {request.FilePath}",
            };
        }

        var bytes = File.ReadAllBytes(request.FilePath);
        referenceImages.Add(new ReferenceImageEntry { FilePath = request.FilePath, RawBytes = bytes });
        int position = referenceImages.Images.Count - 1;
        var applicationEvents = new IApplicationEvent[]
        {
            new ReferenceImageChangedEvent(
                ReferenceImageChangeKind.Loaded,
                $"Loaded image {Path.GetFileName(request.FilePath)}",
                position),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Loaded image [{position}] {Path.GetFileName(request.FilePath)} ({bytes.Length} bytes)",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult<IReadOnlyList<ReferenceImageListEntry>> ListImages(ReferenceImageState referenceImages)
    {
        ArgumentNullException.ThrowIfNull(referenceImages);

        var entries = new List<ReferenceImageListEntry>();
        for (int i = 0; i < referenceImages.Images.Count; i++)
        {
            var image = referenceImages.Images[i];
            entries.Add(new ReferenceImageListEntry(i, image.Label, image.RawBytes.Length));
        }

        return new ApplicationServiceResult<IReadOnlyList<ReferenceImageListEntry>>
        {
            Success = true,
            Message = entries.Count == 0 ? "No reference images loaded." : "Reference images.",
            Data = entries,
        };
    }

    public ApplicationServiceResult RemoveImage(
        ReferenceImageState referenceImages,
        IEventPublisher events,
        RemoveReferenceAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(referenceImages);
        ArgumentNullException.ThrowIfNull(events);

        if (referenceImages.Get(request.Position) is null)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"No image at index {request.Position}.",
            };
        }

        referenceImages.RemoveAt(request.Position);
        var applicationEvents = new IApplicationEvent[]
        {
            new ReferenceImageChangedEvent(
                ReferenceImageChangeKind.Removed,
                $"Removed image [{request.Position}]",
                request.Position),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Removed image [{request.Position}].",
            Events = applicationEvents,
        };
    }
}
