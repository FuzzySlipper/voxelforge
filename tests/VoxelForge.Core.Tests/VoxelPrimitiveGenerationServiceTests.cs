using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.Tests;

public sealed class VoxelPrimitiveGenerationServiceTests
{
    [Fact]
    public void Block_GeneratesOneAssignmentAndBounds()
    {
        var result = Build(new VoxelPrimitiveRequest
        {
            Id = "single",
            Kind = VoxelPrimitiveKind.Block,
            At = new VoxelPrimitivePoint(2, 3, 4),
            PaletteIndex = 7,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Intent);
        VoxelAssignment assignment = Assert.Single(result.Intent.Assignments);
        Assert.Equal(new Point3(2, 3, 4), assignment.Position);
        Assert.Equal((byte)7, assignment.PaletteIndex);

        VoxelPrimitiveSummary summary = Assert.Single(result.Summaries);
        Assert.Equal("single", summary.Id);
        Assert.Equal(VoxelPrimitiveKind.Block, summary.Kind);
        Assert.Equal(1, summary.GeneratedVoxelCount);
        Assert.Equal(1, summary.UniqueVoxelCountAfterBatchMerge);
        Assert.NotNull(summary.Bounds);
        Assert.Equal(new Point3(2, 3, 4), summary.Bounds.Min);
        Assert.Equal(new Point3(2, 3, 4), summary.Bounds.Max);
    }

    [Fact]
    public void Box_NormalizesReversedCornersAndGeneratesFilledCuboid()
    {
        var result = Build(new VoxelPrimitiveRequest
        {
            Kind = VoxelPrimitiveKind.Box,
            From = new VoxelPrimitivePoint(1, 1, 1),
            To = new VoxelPrimitivePoint(0, 0, 0),
            PaletteIndex = 2,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Intent);
        Assert.Equal(8, result.Intent.Assignments.Count);
        Assert.Contains(new VoxelAssignment(new Point3(0, 0, 0), 2), result.Intent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(1, 1, 1), 2), result.Intent.Assignments);

        VoxelPrimitiveSummary summary = Assert.Single(result.Summaries);
        Assert.Equal(8, summary.GeneratedVoxelCount);
        Assert.Equal(8, summary.UniqueVoxelCountAfterBatchMerge);
        Assert.NotNull(summary.Bounds);
        Assert.Equal(new Point3(0, 0, 0), summary.Bounds.Min);
        Assert.Equal(new Point3(1, 1, 1), summary.Bounds.Max);
    }

    [Fact]
    public void BoxModes_MatchThreeDimensionalAndDegenerateCounts()
    {
        VoxelPrimitiveGenerationResult filled = Build(Box(VoxelBoxMode.Filled, 0, 0, 0, 2, 2, 2));
        VoxelPrimitiveGenerationResult shell = Build(Box(VoxelBoxMode.Shell, 0, 0, 0, 2, 2, 2));
        VoxelPrimitiveGenerationResult edges = Build(Box(VoxelBoxMode.Edges, 0, 0, 0, 2, 2, 2));
        VoxelPrimitiveGenerationResult flatShell = Build(Box(VoxelBoxMode.Shell, 0, 0, 0, 2, 2, 0));
        VoxelPrimitiveGenerationResult lineEdges = Build(Box(VoxelBoxMode.Edges, 0, 0, 0, 3, 0, 0));

        Assert.True(filled.Success, filled.Message);
        Assert.True(shell.Success, shell.Message);
        Assert.True(edges.Success, edges.Message);
        Assert.True(flatShell.Success, flatShell.Message);
        Assert.True(lineEdges.Success, lineEdges.Message);
        Assert.NotNull(filled.Intent);
        Assert.NotNull(shell.Intent);
        Assert.NotNull(edges.Intent);
        Assert.NotNull(flatShell.Intent);
        Assert.NotNull(lineEdges.Intent);

        Assert.Equal(27, filled.Intent.Assignments.Count);
        Assert.Equal(26, shell.Intent.Assignments.Count);
        Assert.Equal(20, edges.Intent.Assignments.Count);
        Assert.Equal(9, flatShell.Intent.Assignments.Count);
        Assert.Equal(4, lineEdges.Intent.Assignments.Count);
    }

    [Fact]
    public void Line_UsesDeterministicDdaAndAwayFromZeroRounding()
    {
        var result = Build(new VoxelPrimitiveRequest
        {
            Kind = VoxelPrimitiveKind.Line,
            From = new VoxelPrimitivePoint(0, 0, 0),
            To = new VoxelPrimitivePoint(2, -1, 0),
            PaletteIndex = 3,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Intent);
        Assert.Equal([
            new VoxelAssignment(new Point3(0, 0, 0), 3),
            new VoxelAssignment(new Point3(1, -1, 0), 3),
            new VoxelAssignment(new Point3(2, -1, 0), 3),
        ], result.Intent.Assignments);
    }

    [Fact]
    public void Line_RadiusExpandsWithChebyshevBrush()
    {
        var result = Build(new VoxelPrimitiveRequest
        {
            Kind = VoxelPrimitiveKind.Line,
            From = new VoxelPrimitivePoint(0, 0, 0),
            To = new VoxelPrimitivePoint(0, 0, 0),
            Radius = 1,
            PaletteIndex = 4,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Intent);
        Assert.Equal(27, result.Intent.Assignments.Count);
        Assert.Contains(new VoxelAssignment(new Point3(-1, -1, -1), 4), result.Intent.Assignments);
        Assert.Contains(new VoxelAssignment(new Point3(1, 1, 1), 4), result.Intent.Assignments);

        VoxelPrimitiveSummary summary = Assert.Single(result.Summaries);
        Assert.Equal(27, summary.GeneratedVoxelCount);
        Assert.Equal(27, summary.UniqueVoxelCountAfterBatchMerge);
        Assert.NotNull(summary.Bounds);
        Assert.Equal(new Point3(-1, -1, -1), summary.Bounds.Min);
        Assert.Equal(new Point3(1, 1, 1), summary.Bounds.Max);
    }

    [Fact]
    public void LaterPrimitiveWinsDuplicateCoordinates()
    {
        var service = new VoxelPrimitiveGenerationService();
        VoxelPrimitiveGenerationResult result = service.BuildIntent(new ApplyVoxelPrimitivesRequest
        {
            Primitives = [
                new VoxelPrimitiveRequest
                {
                    Id = "first",
                    Kind = VoxelPrimitiveKind.Block,
                    At = new VoxelPrimitivePoint(0, 0, 0),
                    PaletteIndex = 1,
                },
                new VoxelPrimitiveRequest
                {
                    Id = "second",
                    Kind = VoxelPrimitiveKind.Block,
                    At = new VoxelPrimitivePoint(0, 0, 0),
                    PaletteIndex = 2,
                },
            ],
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Intent);
        VoxelAssignment assignment = Assert.Single(result.Intent.Assignments);
        Assert.Equal(new Point3(0, 0, 0), assignment.Position);
        Assert.Equal((byte)2, assignment.PaletteIndex);

        Assert.Equal(0, result.Summaries[0].UniqueVoxelCountAfterBatchMerge);
        Assert.Equal(1, result.Summaries[1].UniqueVoxelCountAfterBatchMerge);
    }

    [Fact]
    public void PreviewOnly_ReturnsSummariesWithoutIntent()
    {
        var service = new VoxelPrimitiveGenerationService();
        VoxelPrimitiveGenerationResult result = service.BuildIntent(new ApplyVoxelPrimitivesRequest
        {
            PreviewOnly = true,
            Primitives = [Box(VoxelBoxMode.Filled, 0, 0, 0, 1, 1, 1)],
        });

        Assert.True(result.Success, result.Message);
        Assert.Null(result.Intent);
        Assert.Equal("Preview generated 8 voxel assignment(s) from 1 primitive(s).", result.Message);
        VoxelPrimitiveSummary summary = Assert.Single(result.Summaries);
        Assert.Equal(8, summary.GeneratedVoxelCount);
        Assert.Equal(8, summary.UniqueVoxelCountAfterBatchMerge);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void InvalidRequests_ReturnFailureWithoutIntent(VoxelPrimitiveRequest primitive, string expectedMessagePart)
    {
        var result = Build(primitive);

        Assert.False(result.Success);
        Assert.Null(result.Intent);
        Assert.Empty(result.Summaries);
        Assert.Contains(expectedMessagePart, result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyCapRejectsOversizedBatchWithoutIntent()
    {
        var service = new VoxelPrimitiveGenerationService();
        VoxelPrimitiveGenerationResult result = service.BuildIntent(new ApplyVoxelPrimitivesRequest
        {
            MaxGeneratedVoxels = 10,
            Primitives = [Box(VoxelBoxMode.Filled, 0, 0, 0, 2, 2, 2)],
        });

        Assert.False(result.Success);
        Assert.Null(result.Intent);
        Assert.Empty(result.Summaries);
        Assert.Contains("exceeding max_generated_voxels 10", result.Message, StringComparison.Ordinal);
    }

    public static TheoryData<VoxelPrimitiveRequest, string> InvalidRequests()
    {
        return new TheoryData<VoxelPrimitiveRequest, string>
        {
            {
                new VoxelPrimitiveRequest
                {
                    Kind = VoxelPrimitiveKind.Block,
                    At = new VoxelPrimitivePoint(0, 0, 0),
                    PaletteIndex = 0,
                },
                "invalid palette index"
            },
            {
                new VoxelPrimitiveRequest
                {
                    Kind = VoxelPrimitiveKind.Block,
                    PaletteIndex = 1,
                },
                "block requires at"
            },
            {
                new VoxelPrimitiveRequest
                {
                    Kind = VoxelPrimitiveKind.Box,
                    From = new VoxelPrimitivePoint(0, 0, 0),
                    PaletteIndex = 1,
                },
                "box requires from and to"
            },
            {
                new VoxelPrimitiveRequest
                {
                    Kind = VoxelPrimitiveKind.Line,
                    From = new VoxelPrimitivePoint(0, 0, 0),
                    To = new VoxelPrimitivePoint(1, 1, 1),
                    Radius = 17,
                    PaletteIndex = 1,
                },
                "invalid radius"
            },
            {
                new VoxelPrimitiveRequest
                {
                    Kind = (VoxelPrimitiveKind)999,
                    PaletteIndex = 1,
                },
                "unsupported kind"
            },
            {
                new VoxelPrimitiveRequest
                {
                    Kind = VoxelPrimitiveKind.Box,
                    From = new VoxelPrimitivePoint(0, 0, 0),
                    To = new VoxelPrimitivePoint(1, 1, 1),
                    Mode = (VoxelBoxMode)999,
                    PaletteIndex = 1,
                },
                "unsupported box mode"
            },
        };
    }

    private static VoxelPrimitiveGenerationResult Build(VoxelPrimitiveRequest primitive)
    {
        var service = new VoxelPrimitiveGenerationService();
        return service.BuildIntent(new ApplyVoxelPrimitivesRequest
        {
            Primitives = [primitive],
        });
    }

    private static VoxelPrimitiveRequest Box(
        VoxelBoxMode mode,
        int fromX,
        int fromY,
        int fromZ,
        int toX,
        int toY,
        int toZ)
    {
        return new VoxelPrimitiveRequest
        {
            Kind = VoxelPrimitiveKind.Box,
            From = new VoxelPrimitivePoint(fromX, fromY, fromZ),
            To = new VoxelPrimitivePoint(toX, toY, toZ),
            Mode = mode,
            PaletteIndex = 2,
        };
    }
}
