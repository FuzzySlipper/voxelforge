namespace VoxelForge.Core.Services;

public enum VoxelPrimitiveKind
{
    Block,
    Box,
    Line,
}

public enum VoxelBoxMode
{
    Filled,
    Shell,
    Edges,
}

public readonly record struct VoxelPrimitivePoint(int X, int Y, int Z);

public sealed class VoxelPrimitiveRequest
{
    public string? Id { get; init; }
    public required VoxelPrimitiveKind Kind { get; init; }
    public required int PaletteIndex { get; init; }
    public VoxelPrimitivePoint? At { get; init; }
    public VoxelPrimitivePoint? From { get; init; }
    public VoxelPrimitivePoint? To { get; init; }
    public VoxelBoxMode Mode { get; init; } = VoxelBoxMode.Filled;
    public int Radius { get; init; }
}

public sealed class ApplyVoxelPrimitivesRequest
{
    public required IReadOnlyList<VoxelPrimitiveRequest> Primitives { get; init; }
    public int MaxGeneratedVoxels { get; init; } = VoxelPrimitiveGenerationService.DefaultMaxGeneratedVoxels;
    public bool PreviewOnly { get; init; }
}

public sealed class VoxelPrimitiveBounds
{
    public required Point3 Min { get; init; }
    public required Point3 Max { get; init; }
}

public sealed class VoxelPrimitiveSummary
{
    public string? Id { get; init; }
    public required VoxelPrimitiveKind Kind { get; init; }
    public required int PaletteIndex { get; init; }
    public required int GeneratedVoxelCount { get; init; }
    public required int UniqueVoxelCountAfterBatchMerge { get; init; }
    public VoxelPrimitiveBounds? Bounds { get; init; }
}

public sealed class VoxelPrimitiveGenerationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public VoxelMutationIntent? Intent { get; init; }
    public required IReadOnlyList<VoxelPrimitiveSummary> Summaries { get; init; }
}

/// <summary>
/// Expands compact voxel primitives into a validated mutation intent without
/// owning or mutating editor state.
/// </summary>
public sealed class VoxelPrimitiveGenerationService
{
    public const int DefaultMaxGeneratedVoxels = 8192;
    public const int AbsoluteMaxGeneratedVoxels = 65536;
    public const int MaxLineRadius = 16;

    public VoxelPrimitiveGenerationResult BuildIntent(ApplyVoxelPrimitivesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Primitives is null)
            return Failure("Primitive list is required.");

        if (request.Primitives.Count == 0)
            return Failure("At least one primitive is required.");

        if (request.MaxGeneratedVoxels < 1 || request.MaxGeneratedVoxels > AbsoluteMaxGeneratedVoxels)
        {
            return Failure(
                $"max_generated_voxels must be between 1 and {AbsoluteMaxGeneratedVoxels}.");
        }

        var finalAssignments = new Dictionary<Point3, FinalVoxel>(request.MaxGeneratedVoxels);
        var expandedPrimitives = new List<ExpandedPrimitive>(request.Primitives.Count);

        for (int i = 0; i < request.Primitives.Count; i++)
        {
            VoxelPrimitiveRequest? primitive = request.Primitives[i];
            if (primitive is null)
                return Failure($"Primitive {i} is required.");

            string? validationError = ValidatePrimitive(primitive, i);
            if (validationError is not null)
                return Failure(validationError);

            var expanded = new ExpandedPrimitive(
                primitive.Id,
                primitive.Kind,
                primitive.PaletteIndex);
            expandedPrimitives.Add(expanded);

            string? expansionError = ExpandPrimitive(
                primitive,
                i,
                finalAssignments,
                expanded,
                request.MaxGeneratedVoxels);
            if (expansionError is not null)
                return Failure(expansionError);
        }

        var uniqueCountsByPrimitive = new int[expandedPrimitives.Count];
        foreach (KeyValuePair<Point3, FinalVoxel> assignment in finalAssignments)
            uniqueCountsByPrimitive[assignment.Value.PrimitiveIndex]++;

        var summaries = new List<VoxelPrimitiveSummary>(expandedPrimitives.Count);
        for (int i = 0; i < expandedPrimitives.Count; i++)
        {
            ExpandedPrimitive primitive = expandedPrimitives[i];
            summaries.Add(new VoxelPrimitiveSummary
            {
                Id = primitive.Id,
                Kind = primitive.Kind,
                PaletteIndex = primitive.PaletteIndex,
                GeneratedVoxelCount = primitive.GeneratedVoxelCount,
                UniqueVoxelCountAfterBatchMerge = uniqueCountsByPrimitive[i],
                Bounds = primitive.ToBounds(),
            });
        }

        string action = request.PreviewOnly ? "Preview generated" : "Generated";
        string message = $"{action} {finalAssignments.Count} voxel assignment(s) from {request.Primitives.Count} primitive(s).";
        if (request.PreviewOnly)
        {
            return new VoxelPrimitiveGenerationResult
            {
                Success = true,
                Message = message,
                Summaries = summaries,
            };
        }

        var assignments = new List<VoxelAssignment>(finalAssignments.Count);
        foreach (KeyValuePair<Point3, FinalVoxel> assignment in finalAssignments)
            assignments.Add(new VoxelAssignment(assignment.Key, assignment.Value.PaletteIndex));

        return new VoxelPrimitiveGenerationResult
        {
            Success = true,
            Message = message,
            Intent = new VoxelMutationIntent
            {
                Assignments = assignments,
                Description = message,
            },
            Summaries = summaries,
        };
    }

    private static string? ValidatePrimitive(VoxelPrimitiveRequest primitive, int primitiveIndex)
    {
        if (!IsSupportedKind(primitive.Kind))
            return $"Primitive {primitiveIndex} has unsupported kind '{primitive.Kind}'.";

        if (!IsSupportedMode(primitive.Mode))
            return $"Primitive {primitiveIndex} has unsupported box mode '{primitive.Mode}'.";

        if (primitive.PaletteIndex < 1 || primitive.PaletteIndex > 255)
        {
            return $"Primitive {primitiveIndex} has invalid palette index {primitive.PaletteIndex}. Expected 1-255.";
        }

        if (primitive.Radius < 0 || primitive.Radius > MaxLineRadius)
        {
            return $"Primitive {primitiveIndex} has invalid radius {primitive.Radius}. Expected 0-{MaxLineRadius}.";
        }

        return primitive.Kind switch
        {
            VoxelPrimitiveKind.Block when primitive.At is null => $"Primitive {primitiveIndex} block requires at.",
            VoxelPrimitiveKind.Box when primitive.From is null || primitive.To is null => $"Primitive {primitiveIndex} box requires from and to.",
            VoxelPrimitiveKind.Line when primitive.From is null || primitive.To is null => $"Primitive {primitiveIndex} line requires from and to.",
            _ => null,
        };
    }

    private static bool IsSupportedKind(VoxelPrimitiveKind kind)
    {
        return kind is VoxelPrimitiveKind.Block or VoxelPrimitiveKind.Box or VoxelPrimitiveKind.Line;
    }

    private static bool IsSupportedMode(VoxelBoxMode mode)
    {
        return mode is VoxelBoxMode.Filled or VoxelBoxMode.Shell or VoxelBoxMode.Edges;
    }

    private static string? ExpandPrimitive(
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        return primitive.Kind switch
        {
            VoxelPrimitiveKind.Block => ExpandBlock(
                primitive.At!.Value,
                primitive,
                primitiveIndex,
                finalAssignments,
                expanded,
                maxGeneratedVoxels),
            VoxelPrimitiveKind.Box => ExpandBox(
                primitive.From!.Value,
                primitive.To!.Value,
                primitive,
                primitiveIndex,
                finalAssignments,
                expanded,
                maxGeneratedVoxels),
            VoxelPrimitiveKind.Line => ExpandLine(
                primitive.From!.Value,
                primitive.To!.Value,
                primitive,
                primitiveIndex,
                finalAssignments,
                expanded,
                maxGeneratedVoxels),
            _ => $"Primitive {primitiveIndex} has unsupported kind '{primitive.Kind}'.",
        };
    }

    private static string? ExpandBlock(
        VoxelPrimitivePoint at,
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        return AddGeneratedVoxel(
            ToPoint3(at),
            primitive,
            primitiveIndex,
            finalAssignments,
            expanded,
            maxGeneratedVoxels);
    }

    private static string? ExpandBox(
        VoxelPrimitivePoint from,
        VoxelPrimitivePoint to,
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        int minX = Math.Min(from.X, to.X);
        int minY = Math.Min(from.Y, to.Y);
        int minZ = Math.Min(from.Z, to.Z);
        int maxX = Math.Max(from.X, to.X);
        int maxY = Math.Max(from.Y, to.Y);
        int maxZ = Math.Max(from.Z, to.Z);

        for (long z = minZ; z <= maxZ; z++)
        {
            for (long y = minY; y <= maxY; y++)
            {
                for (long x = minX; x <= maxX; x++)
                {
                    if (!IncludesBoxPoint(primitive.Mode, x, y, z, minX, minY, minZ, maxX, maxY, maxZ))
                        continue;

                    string? error = AddGeneratedVoxel(
                        new Point3((int)x, (int)y, (int)z),
                        primitive,
                        primitiveIndex,
                        finalAssignments,
                        expanded,
                        maxGeneratedVoxels);
                    if (error is not null)
                        return error;
                }
            }
        }

        return null;
    }

    private static bool IncludesBoxPoint(
        VoxelBoxMode mode,
        long x,
        long y,
        long z,
        long minX,
        long minY,
        long minZ,
        long maxX,
        long maxY,
        long maxZ)
    {
        if (mode == VoxelBoxMode.Filled)
            return true;

        int boundaryAxes = 0;
        if (x == minX || x == maxX)
            boundaryAxes++;
        if (y == minY || y == maxY)
            boundaryAxes++;
        if (z == minZ || z == maxZ)
            boundaryAxes++;

        return mode == VoxelBoxMode.Shell
            ? boundaryAxes >= 1
            : boundaryAxes >= 2;
    }

    private static string? ExpandLine(
        VoxelPrimitivePoint from,
        VoxelPrimitivePoint to,
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        long dx = (long)to.X - from.X;
        long dy = (long)to.Y - from.Y;
        long dz = (long)to.Z - from.Z;
        long steps = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));

        var pathPoints = new HashSet<Point3>();
        for (long step = 0; step <= steps; step++)
        {
            Point3 center = RasterizeLinePoint(from, dx, dy, dz, step, steps);
            if (!pathPoints.Add(center))
                continue;

            string? error = AddLineBrush(
                center,
                primitive,
                primitiveIndex,
                finalAssignments,
                expanded,
                maxGeneratedVoxels);
            if (error is not null)
                return error;
        }

        return null;
    }

    private static Point3 RasterizeLinePoint(
        VoxelPrimitivePoint from,
        long dx,
        long dy,
        long dz,
        long step,
        long steps)
    {
        if (steps == 0)
            return ToPoint3(from);

        long x = from.X + RoundAwayFromZero(dx, step, steps);
        long y = from.Y + RoundAwayFromZero(dy, step, steps);
        long z = from.Z + RoundAwayFromZero(dz, step, steps);
        return new Point3((int)x, (int)y, (int)z);
    }

    private static long RoundAwayFromZero(long delta, long step, long steps)
    {
        double value = (double)delta * step / steps;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static string? AddLineBrush(
        Point3 center,
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        int radius = primitive.Radius;
        for (long z = (long)center.Z - radius; z <= (long)center.Z + radius; z++)
        {
            for (long y = (long)center.Y - radius; y <= (long)center.Y + radius; y++)
            {
                for (long x = (long)center.X - radius; x <= (long)center.X + radius; x++)
                {
                    if (!IsSupportedCoordinate(x) || !IsSupportedCoordinate(y) || !IsSupportedCoordinate(z))
                        return $"Primitive {primitiveIndex} radius expands outside supported coordinate range.";

                    string? error = AddGeneratedVoxel(
                        new Point3((int)x, (int)y, (int)z),
                        primitive,
                        primitiveIndex,
                        finalAssignments,
                        expanded,
                        maxGeneratedVoxels);
                    if (error is not null)
                        return error;
                }
            }
        }

        return null;
    }

    private static string? AddGeneratedVoxel(
        Point3 position,
        VoxelPrimitiveRequest primitive,
        int primitiveIndex,
        Dictionary<Point3, FinalVoxel> finalAssignments,
        ExpandedPrimitive expanded,
        int maxGeneratedVoxels)
    {
        expanded.Record(position);
        finalAssignments[position] = new FinalVoxel(primitiveIndex, (byte)primitive.PaletteIndex);
        if (finalAssignments.Count > maxGeneratedVoxels)
        {
            return $"Primitive batch generated {finalAssignments.Count} unique voxel assignment(s), exceeding max_generated_voxels {maxGeneratedVoxels}.";
        }

        return null;
    }

    private static Point3 ToPoint3(VoxelPrimitivePoint point)
    {
        return new Point3(point.X, point.Y, point.Z);
    }

    private static bool IsSupportedCoordinate(long value)
    {
        return value >= int.MinValue && value <= int.MaxValue;
    }

    private static VoxelPrimitiveGenerationResult Failure(string message)
    {
        return new VoxelPrimitiveGenerationResult
        {
            Success = false,
            Message = message,
            Summaries = [],
        };
    }

    private readonly record struct FinalVoxel(int PrimitiveIndex, byte PaletteIndex);

    private sealed class ExpandedPrimitive
    {
        private Point3 _min;
        private Point3 _max;
        private bool _hasBounds;

        public ExpandedPrimitive(string? id, VoxelPrimitiveKind kind, int paletteIndex)
        {
            Id = id;
            Kind = kind;
            PaletteIndex = paletteIndex;
        }

        public string? Id { get; }
        public VoxelPrimitiveKind Kind { get; }
        public int PaletteIndex { get; }
        public int GeneratedVoxelCount { get; private set; }

        public void Record(Point3 point)
        {
            GeneratedVoxelCount++;
            if (!_hasBounds)
            {
                _min = point;
                _max = point;
                _hasBounds = true;
                return;
            }

            _min = new Point3(
                Math.Min(_min.X, point.X),
                Math.Min(_min.Y, point.Y),
                Math.Min(_min.Z, point.Z));
            _max = new Point3(
                Math.Max(_max.X, point.X),
                Math.Max(_max.Y, point.Y),
                Math.Max(_max.Z, point.Z));
        }

        public VoxelPrimitiveBounds? ToBounds()
        {
            if (!_hasBounds)
                return null;

            return new VoxelPrimitiveBounds
            {
                Min = _min,
                Max = _max,
            };
        }
    }
}
