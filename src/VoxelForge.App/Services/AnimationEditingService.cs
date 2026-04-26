using VoxelForge.App.Events;

namespace VoxelForge.App.Services;

public readonly record struct AddAnimationFrameRequest(int ClipIndex);

/// <summary>
/// Stateless service for animation clip/frame editing operations.
/// </summary>
public sealed class AnimationEditingService
{
    public ApplicationServiceResult AddFrame(
        EditorDocumentState document,
        IEventPublisher events,
        AddAnimationFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(events);

        if (request.ClipIndex < 0 || request.ClipIndex >= document.Clips.Count)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"No animation clip at index {request.ClipIndex}.",
            };
        }

        var clip = document.Clips[request.ClipIndex];
        clip.AddFrame();
        int frameIndex = clip.Frames.Count - 1;
        var applicationEvents = new IApplicationEvent[]
        {
            new AnimationChangedEvent(
                AnimationChangeKind.FrameAdded,
                $"Added frame {frameIndex} to clip {request.ClipIndex}",
                request.ClipIndex,
                frameIndex),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Added frame {frameIndex} to clip {request.ClipIndex}.",
            Events = applicationEvents,
        };
    }
}
