using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Services;

namespace VoxelForge.Core.Tests;

public sealed class EditorConfigServiceTests
{
    [Fact]
    public void SetValue_ParsesBackgroundColorAndPublishesEvent()
    {
        var config = new EditorConfigState();
        var events = new ApplicationEventDispatcher();
        var handler = new RecordingConfigHandler();
        events.Register<ConfigChangedEvent>(handler);

        var result = new EditorConfigService().SetValue(
            config,
            events,
            new SetConfigValueRequest("backgroundColor", "1,2,3", Save: false));

        Assert.True(result.Success);
        Assert.Equal([1, 2, 3], config.BackgroundColor);
        Assert.Single(handler.Events);
        Assert.Equal("backgroundcolor", handler.Events[0].Key);
        Assert.False(handler.Events[0].Saved);
    }

    [Fact]
    public void SetMeasureGrid_RejectsNonPositiveScale()
    {
        var config = new EditorConfigState();
        var result = new EditorConfigService().SetMeasureGrid(
            config,
            new ApplicationEventDispatcher(),
            new SetMeasureGridRequest(true, 0));

        Assert.False(result.Success);
        Assert.False(config.ShowMeasureGrid);
    }

    private sealed class RecordingConfigHandler : IEventHandler<ConfigChangedEvent>
    {
        public List<ConfigChangedEvent> Events { get; } = [];

        public void Handle(ConfigChangedEvent applicationEvent)
        {
            Events.Add(applicationEvent);
        }
    }
}
