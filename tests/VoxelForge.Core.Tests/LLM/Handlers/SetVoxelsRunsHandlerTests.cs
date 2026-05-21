using System.Text.Json;
using VoxelForge.Core;
using VoxelForge.Core.LLM;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.Tests.LLM.Handlers;

public sealed class SetVoxelsRunsHandlerTests
{
    private static SetVoxelsRunsHandler CreateHandler()
    {
        return new SetVoxelsRunsHandler(new VoxelMutationIntentService());
    }

    private static ToolHandlerResult Invoke(SetVoxelsRunsHandler handler, string json)
    {
        using var doc = JsonDocument.Parse(json);
        // Handler doesn't actually use model/labels/clips for this mutation,
        // but we pass non-null to satisfy the interface.
        return handler.Handle(
            doc.RootElement,
            null!,
            null!,
            null!);
    }

    [Fact]
    public void SingleRun_Sets16Voxels()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[{"x1":0,"x2":15,"y":5,"z":3,"i":2}]}""");

        Assert.False(result.IsError);
        Assert.Contains("16", result.Content);
        Assert.NotNull(result.MutationIntent);
        Assert.Equal(16, result.MutationIntent.Assignments.Count);

        // Verify a few positions
        Assert.Contains(new VoxelAssignment(new Point3(0, 5, 3), 2), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(7, 5, 3), 2), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(15, 5, 3), 2), result.MutationIntent.Assignments);
    }

    [Fact]
    public void MultipleRuns_SetsCorrectTotal()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """
        {
            "runs": [
                {"x1": 0, "x2": 7, "y": 0, "z": 0, "i": 1},
                {"x1": 10, "x2": 12, "y": 0, "z": 0, "i": 2},
                {"x1": 20, "x2": 24, "y": 1, "z": 1, "i": 3}
            ]
        }
        """);

        Assert.False(result.IsError);
        Assert.NotNull(result.MutationIntent);
        // 8 + 3 + 5 = 16
        Assert.Equal(16, result.MutationIntent.Assignments.Count);
        Assert.Contains(new VoxelAssignment(new Point3(0, 0, 0), 1), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(7, 0, 0), 1), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(10, 0, 0), 2), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(12, 0, 0), 2), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(20, 1, 1), 3), result.MutationIntent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(24, 1, 1), 3), result.MutationIntent.Assignments);
    }

    [Fact]
    public void EmptyRunsArray_ReturnsSuccessWithZeroVoxels()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[]}""");

        Assert.False(result.IsError);
        Assert.NotNull(result.MutationIntent);
        Assert.Empty(result.MutationIntent.Assignments);
        Assert.Contains("0", result.Content);
    }

    [Fact]
    public void Invalid_X1GreaterThanX2_ReturnsError()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[{"x1":10,"x2":5,"y":0,"z":0,"i":1}]}""");

        Assert.True(result.IsError);
        Assert.Contains("x1 (10) > x2 (5)", result.Content);
        Assert.Null(result.MutationIntent);
    }

    [Fact]
    public void Invalid_PaletteIndexZero_ReturnsError()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[{"x1":0,"x2":5,"y":0,"z":0,"i":0}]}""");

        Assert.True(result.IsError);
        Assert.Contains("Invalid palette index 0", result.Content);
        Assert.Null(result.MutationIntent);
    }

    [Fact]
    public void Invalid_PaletteIndexAbove255_ReturnsError()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[{"x1":0,"x2":5,"y":0,"z":0,"i":256}]}""");

        Assert.True(result.IsError);
        Assert.Contains("Invalid palette index 256", result.Content);
        Assert.Null(result.MutationIntent);
    }

    [Fact]
    public void SafetyCap_ExceedsMax_ReturnsError()
    {
        var handler = CreateHandler();
        // 65536 + 1 voxels across runs — total 2 runs with 65536 and 1 voxels each = 65537
        // Actually, let's do 2 runs: one that sets 65536 voxels and another with 1
        // x1=0,x2=65535 gives 65536 voxels, then x1=0,x2=0 adds 1 more = 65537
        var result = Invoke(handler, """
        {
            "runs": [
                {"x1": 0, "x2": 65535, "y": 0, "z": 0, "i": 1},
                {"x1": 0, "x2": 0, "y": 1, "z": 0, "i": 2}
            ]
        }
        """);

        Assert.True(result.IsError);
        Assert.Contains("Safety cap exceeded", result.Content);
        Assert.Null(result.MutationIntent);
    }

    [Fact]
    public void SafetyCap_AtLimit_ReturnsSuccess()
    {
        var handler = CreateHandler();
        // Exactly 65536 voxels: x1=0,x2=65535 = 65536
        var result = Invoke(handler, """{"runs":[{"x1":0,"x2":65535,"y":0,"z":0,"i":1}]}""");

        Assert.False(result.IsError);
        Assert.NotNull(result.MutationIntent);
        Assert.Equal(65536, result.MutationIntent.Assignments.Count);
    }

    [Fact]
    public void SingleVoxelRun_LengthOne()
    {
        var handler = CreateHandler();
        var result = Invoke(handler, """{"runs":[{"x1":7,"x2":7,"y":10,"z":20,"i":5}]}""");

        Assert.False(result.IsError);
        Assert.NotNull(result.MutationIntent);
        var assignment = Assert.Single(result.MutationIntent.Assignments);
        Assert.Equal(new Point3(7, 10, 20), assignment.Position);
        Assert.Equal((byte)5, assignment.PaletteIndex);
    }
}
