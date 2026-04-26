using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

public sealed class AnimationEditingServiceTests
{
    [Fact]
    public void AddFrame_AddsFrameAndPublishesTypedEvent()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        var clip = new AnimationClip(model, NullLogger<AnimationClip>.Instance) { Name = "idle" };
        var document = new EditorDocumentState(model, new LabelIndex(NullLogger<LabelIndex>.Instance), [clip]);
        var events = new ApplicationEventDispatcher();
        var handler = new RecordingAnimationHandler();
        events.Register<AnimationChangedEvent>(handler);

        var result = new AnimationEditingService().AddFrame(
            document,
            events,
            new AddAnimationFrameRequest(0));

        Assert.True(result.Success);
        Assert.Single(clip.Frames);
        Assert.Single(handler.Events);
        Assert.Equal(AnimationChangeKind.FrameAdded, handler.Events[0].Kind);
        Assert.Equal(0, handler.Events[0].ClipIndex);
        Assert.Equal(0, handler.Events[0].FrameIndex);
    }

    [Fact]
    public void AddFrame_InvalidClipFailsWithoutMutation()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        var document = new EditorDocumentState(model, new LabelIndex(NullLogger<LabelIndex>.Instance));

        var result = new AnimationEditingService().AddFrame(
            document,
            new ApplicationEventDispatcher(),
            new AddAnimationFrameRequest(0));

        Assert.False(result.Success);
        Assert.Empty(document.Clips);
    }

    private sealed class RecordingAnimationHandler : IEventHandler<AnimationChangedEvent>
    {
        public List<AnimationChangedEvent> Events { get; } = [];

        public void Handle(AnimationChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
