using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.App.Tests;

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

    [Theory]
    [InlineData("invertOrbitX", "true", "True")]
    [InlineData("invertOrbitY", "true", "True")]
    [InlineData("orbitSensitivity", "2.5", "2.5")]
    [InlineData("zoomSensitivity", "0.75", "0.75")]
    [InlineData("defaultGridHint", "64", "64")]
    [InlineData("maxUndoDepth", "25", "25")]
    [InlineData("maxZoomDistance", "150", "150")]
    [InlineData("voxelsPerMeter", "12.5", "12.5")]
    [InlineData("backgroundColor", "4,5,6", "4,5,6")]
    [InlineData("showMeasureGrid", "true", "True")]
    public void SetValue_UsesSharedConfigKeyMapForEveryListedEntry(string key, string value, string expectedValue)
    {
        var config = new EditorConfigState();
        var service = new EditorConfigService();

        var setResult = service.SetValue(
            config,
            new ApplicationEventDispatcher(),
            new SetConfigValueRequest(key, value, Save: false));
        var listResult = service.List(config);

        Assert.True(setResult.Success);
        Assert.NotNull(listResult.Data);
        Assert.Contains(listResult.Data, entry => entry.Key == key && entry.Value == expectedValue);
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
