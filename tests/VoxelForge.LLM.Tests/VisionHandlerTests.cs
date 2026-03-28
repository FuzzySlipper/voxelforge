using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.LLM.Tests;

public sealed class VisionHandlerTests
{
    private static VoxelModel CreateModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        model.SetVoxel(new Point3(0, 0, 0), 1);
        return model;
    }

    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void ViewModelHandler_ReturnsBase64Image()
    {
        var fakeProvider = new FakeScreenshotProvider();
        var handler = new ViewModelHandler(() => fakeProvider);

        var result = handler.Handle(EmptyArgs, CreateModel(), new LabelIndex(NullLogger<LabelIndex>.Instance), []);

        Assert.False(result.IsError);
        Assert.StartsWith("[image:png;base64]", result.Content);
        var base64 = result.Content["[image:png;base64]".Length..];
        var bytes = Convert.FromBase64String(base64);
        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void ViewModelHandler_NoProvider_ReturnsError()
    {
        var handler = new ViewModelHandler(() => null);

        var result = handler.Handle(EmptyArgs, CreateModel(), new LabelIndex(NullLogger<LabelIndex>.Instance), []);

        Assert.True(result.IsError);
        Assert.Contains("not available", result.Content);
    }

    [Fact]
    public void ViewFromAngleHandler_ReturnsBase64Image()
    {
        var fakeProvider = new FakeScreenshotProvider();
        var handler = new ViewFromAngleHandler(() => fakeProvider);

        var args = JsonDocument.Parse("""{"yaw": 1.57, "pitch": 0.5}""").RootElement;
        var result = handler.Handle(args, CreateModel(), new LabelIndex(NullLogger<LabelIndex>.Instance), []);

        Assert.False(result.IsError);
        Assert.Contains("[image:png;base64]", result.Content);
    }

    [Fact]
    public void CompareReferenceHandler_Returns5Images()
    {
        var fakeProvider = new FakeScreenshotProvider();
        var handler = new CompareReferenceHandler(() => fakeProvider);

        var result = handler.Handle(EmptyArgs, CreateModel(), new LabelIndex(NullLogger<LabelIndex>.Instance), []);

        Assert.False(result.IsError);
        Assert.Contains("[front:", result.Content);
        Assert.Contains("[back:", result.Content);
        Assert.Contains("[left:", result.Content);
        Assert.Contains("[right:", result.Content);
        Assert.Contains("[top:", result.Content);
    }

    private sealed class FakeScreenshotProvider : IScreenshotProvider
    {
        // Return a minimal valid-ish byte array (not a real PNG, but tests base64 encoding)
        private static byte[] FakeImage() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public byte[] CaptureViewport() => FakeImage();
        public byte[] CaptureFromAngle(float yaw, float pitch) => FakeImage();
        public byte[][] CaptureMultiAngle() => [FakeImage(), FakeImage(), FakeImage(), FakeImage(), FakeImage()];
    }
}
