using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;
using VoxelForge.Core.LLM;
using VoxelForge.Core.LLM.Handlers;

namespace VoxelForge.LLM.Tests;

public sealed class ToolLoopTests
{
    private static VoxelModel CreateModel()
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance);
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        return model;
    }

    private static LabelIndex CreateLabels() => new(NullLogger<LabelIndex>.Instance);

    private static ToolLoop CreateLoop(ICompletionService completion, params IToolHandler[] handlers)
    {
        return new ToolLoop(completion, handlers, NullLogger<ToolLoop>.Instance);
    }

    [Fact]
    public async Task SetVoxels_ToolCall_ModifiesModel()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("set_voxels", "call_1", """{"voxels":[{"x":0,"y":0,"z":0,"i":1},{"x":1,"y":0,"z":0,"i":1}]}""");
        fake.EnqueueTextResponse("Done! I set 2 voxels.");

        var model = CreateModel();
        var loop = CreateLoop(fake, new SetVoxelsHandler());

        var result = await loop.RunAsync("system", "place 2 voxels", model, CreateLabels(), [], ct: CancellationToken.None);

        // Apply the deferred actions
        foreach (var action in result.ApplyActions)
            action();

        Assert.Equal(2, model.GetVoxelCount());
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(1, 0, 0)));
        Assert.Equal("Done! I set 2 voxels.", result.ResponseText);
    }

    [Fact]
    public async Task UnknownTool_ReturnsError_NoException()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("nonexistent_tool", "call_1", "{}");
        fake.EnqueueTextResponse("Sorry, that tool doesn't exist.");

        var model = CreateModel();
        var loop = CreateLoop(fake);

        var result = await loop.RunAsync("system", "do something", model, CreateLabels(), []);

        // Should complete without throwing
        Assert.Equal("Sorry, that tool doesn't exist.", result.ResponseText);
        // Second request should contain the error tool result
        Assert.Equal(2, fake.ReceivedRequests.Count);
    }

    [Fact]
    public async Task NoToolCalls_ExitsAfterOneRound()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueTextResponse("Here's what I see.");

        var model = CreateModel();
        var loop = CreateLoop(fake, new DescribeModelHandler());

        var result = await loop.RunAsync("system", "describe", model, CreateLabels(), []);

        Assert.Equal("Here's what I see.", result.ResponseText);
        Assert.Single(fake.ReceivedRequests);
    }

    [Fact]
    public async Task GetVoxelsInArea_ReturnsCorrectVoxels()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(5, 5, 5), 1);
        model.SetVoxel(new Point3(10, 10, 10), 1);

        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("get_voxels_in_area", "call_1",
            """{"min_x":0,"min_y":0,"min_z":0,"max_x":7,"max_y":7,"max_z":7}""");
        fake.EnqueueTextResponse("Found the voxel.");

        var loop = CreateLoop(fake, new GetVoxelsInAreaHandler());
        await loop.RunAsync("system", "look around", model, CreateLabels(), []);

        // Check the tool result sent back to the LLM
        var secondRequest = fake.ReceivedRequests[1];
        var toolResultMsg = secondRequest.Messages.Last(m => m.Role == "tool");
        Assert.NotNull(toolResultMsg.ToolResults);
        Assert.Contains("\"count\":1", toolResultMsg.ToolResults[0].Content);
    }

    [Fact]
    public async Task DescribeModel_ReturnsNonEmptyDescription()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var handler = new DescribeModelHandler();
        var result = handler.Handle(
            System.Text.Json.JsonDocument.Parse("{}").RootElement,
            model, CreateLabels(), []);

        Assert.False(string.IsNullOrEmpty(result.Content));
        Assert.Contains("1 voxels", result.Content);
        Assert.Contains("Stone", result.Content);
    }
}
