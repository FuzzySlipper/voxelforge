namespace VoxelForge.Core.Services;

public enum SpatialConnectivity
{
    Six = 6,
    TwentySix = 26,
}

public enum CrossSectionAxis
{
    X,
    Y,
    Z,
}

public enum SpatialCollisionShapeKind
{
    Box,
    Region,
}

public readonly record struct SpatialBox(Point3 Min, Point3 Max)
{
    public static SpatialBox FromCorners(Point3 first, Point3 second)
    {
        return new SpatialBox(
            new Point3(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y), Math.Min(first.Z, second.Z)),
            new Point3(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y), Math.Max(first.Z, second.Z)));
    }

    public bool Contains(Point3 point)
    {
        return point.X >= Min.X && point.X <= Max.X &&
            point.Y >= Min.Y && point.Y <= Max.Y &&
            point.Z >= Min.Z && point.Z <= Max.Z;
    }

    public SpatialBox? Intersect(SpatialBox other)
    {
        var min = new Point3(
            Math.Max(Min.X, other.Min.X),
            Math.Max(Min.Y, other.Min.Y),
            Math.Max(Min.Z, other.Min.Z));
        var max = new Point3(
            Math.Min(Max.X, other.Max.X),
            Math.Min(Max.Y, other.Max.Y),
            Math.Min(Max.Z, other.Max.Z));

        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            return null;

        return new SpatialBox(min, max);
    }

    public long VoxelCount()
    {
        long x = (long)Max.X - Min.X + 1;
        long y = (long)Max.Y - Min.Y + 1;
        long z = (long)Max.Z - Min.Z + 1;
        if (x <= 0 || y <= 0 || z <= 0)
            return 0;

        if (x > long.MaxValue / y)
            return long.MaxValue;
        long xy = x * y;
        if (xy > long.MaxValue / z)
            return long.MaxValue;
        return xy * z;
    }
}

public sealed class RegionNeighborInfo
{
    public required RegionId RegionId { get; init; }
    public required string Name { get; init; }
    public required int InterfacePairCount { get; init; }
    public required IReadOnlyList<Point3> SourceBoundaryVoxels { get; init; }
    public required IReadOnlyList<Point3> NeighborBoundaryVoxels { get; init; }
}

public sealed class RegionNeighborsResult
{
    public required RegionId RegionId { get; init; }
    public required string Name { get; init; }
    public required SpatialConnectivity Connectivity { get; init; }
    public required IReadOnlyList<RegionNeighborInfo> Neighbors { get; init; }
}

public readonly record struct RegionInterfacePair(Point3 RegionAVoxel, Point3 RegionBVoxel);

public sealed class RegionInterfaceResult
{
    public required RegionId RegionAId { get; init; }
    public required string RegionAName { get; init; }
    public required RegionId RegionBId { get; init; }
    public required string RegionBName { get; init; }
    public required SpatialConnectivity Connectivity { get; init; }
    public required IReadOnlyList<RegionInterfacePair> Pairs { get; init; }
    public required IReadOnlyList<Point3> RegionABoundaryVoxels { get; init; }
    public required IReadOnlyList<Point3> RegionBBoundaryVoxels { get; init; }
}

public readonly record struct SpatialPointD(double X, double Y, double Z);

public sealed class RegionCentroidDistanceResult
{
    public required RegionId RegionAId { get; init; }
    public required string RegionAName { get; init; }
    public required SpatialPointD RegionACentroid { get; init; }
    public required RegionId RegionBId { get; init; }
    public required string RegionBName { get; init; }
    public required SpatialPointD RegionBCentroid { get; init; }
    public required double Distance { get; init; }
}

public sealed class RegionNearestDistanceResult
{
    public required RegionId RegionAId { get; init; }
    public required string RegionAName { get; init; }
    public required Point3 RegionAVoxel { get; init; }
    public required RegionId RegionBId { get; init; }
    public required string RegionBName { get; init; }
    public required Point3 RegionBVoxel { get; init; }
    public required double VoxelCenterDistance { get; init; }
    public required double SurfaceDistance { get; init; }
}

public sealed class CrossSectionCell
{
    public required Point3 Position { get; init; }
    public required byte PaletteIndex { get; init; }
    public RegionId? RegionId { get; init; }
    public string? RegionName { get; init; }
}

public sealed class CrossSectionResult
{
    public required CrossSectionAxis Axis { get; init; }
    public required int Index { get; init; }
    public required string UAxis { get; init; }
    public required string VAxis { get; init; }
    public SpatialBox? ModelBounds { get; init; }
    public required IReadOnlyList<CrossSectionCell> Cells { get; init; }
}

public sealed class SpatialCollisionShape
{
    private SpatialCollisionShape(SpatialCollisionShapeKind kind, SpatialBox? box, RegionId? regionId)
    {
        Kind = kind;
        Box = box;
        RegionId = regionId;
    }

    public SpatialCollisionShapeKind Kind { get; }

    public SpatialBox? Box { get; }

    public RegionId? RegionId { get; }

    public static SpatialCollisionShape FromBox(SpatialBox box)
    {
        return new SpatialCollisionShape(SpatialCollisionShapeKind.Box, box, null);
    }

    public static SpatialCollisionShape FromRegion(RegionId regionId)
    {
        return new SpatialCollisionShape(SpatialCollisionShapeKind.Region, null, regionId);
    }
}

public sealed class SpatialCollisionResult
{
    public required bool Collides { get; init; }
    public required long OverlapVoxelCount { get; init; }
    public SpatialBox? IntersectionBox { get; init; }
    public required IReadOnlyList<Point3> OverlapVoxels { get; init; }
}

/// <summary>
/// Stateless domain service for higher-level spatial reasoning over labeled voxel models.
/// </summary>
public sealed class SpatialQueryService
{
    public RegionNeighborsResult GetRegionNeighbors(LabelIndex labels, RegionId regionId, SpatialConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var region = GetRegion(labels, regionId);
        var offsets = BuildOffsets(connectivity);
        var accumulators = new Dictionary<RegionId, NeighborAccumulator>();

        foreach (var sourcePoint in region.Voxels)
        {
            for (int i = 0; i < offsets.Count; i++)
            {
                var neighborPoint = Add(sourcePoint, offsets[i]);
                var neighborId = labels.GetRegion(neighborPoint);
                if (!neighborId.HasValue || neighborId.Value == regionId)
                    continue;

                if (!labels.Regions.TryGetValue(neighborId.Value, out var neighborRegion))
                    continue;

                if (!accumulators.TryGetValue(neighborId.Value, out var accumulator))
                {
                    accumulator = new NeighborAccumulator(neighborId.Value, neighborRegion.Name);
                    accumulators[neighborId.Value] = accumulator;
                }

                accumulator.InterfacePairCount++;
                accumulator.SourceBoundaryVoxels.Add(sourcePoint);
                accumulator.NeighborBoundaryVoxels.Add(neighborPoint);
            }
        }

        var neighborIds = new List<RegionId>(accumulators.Keys);
        neighborIds.Sort(CompareRegionIds);
        var neighbors = new List<RegionNeighborInfo>();
        for (int i = 0; i < neighborIds.Count; i++)
        {
            var accumulator = accumulators[neighborIds[i]];
            neighbors.Add(new RegionNeighborInfo
            {
                RegionId = accumulator.RegionId,
                Name = accumulator.Name,
                InterfacePairCount = accumulator.InterfacePairCount,
                SourceBoundaryVoxels = SortPoints(accumulator.SourceBoundaryVoxels),
                NeighborBoundaryVoxels = SortPoints(accumulator.NeighborBoundaryVoxels),
            });
        }

        return new RegionNeighborsResult
        {
            RegionId = regionId,
            Name = region.Name,
            Connectivity = connectivity,
            Neighbors = neighbors,
        };
    }

    public RegionInterfaceResult GetInterfaceVoxels(LabelIndex labels, RegionId regionAId, RegionId regionBId, SpatialConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var regionA = GetRegion(labels, regionAId);
        var regionB = GetRegion(labels, regionBId);
        var offsets = BuildOffsets(connectivity);
        var pairs = new List<RegionInterfacePair>();
        var boundaryA = new HashSet<Point3>();
        var boundaryB = new HashSet<Point3>();

        foreach (var sourcePoint in regionA.Voxels)
        {
            for (int i = 0; i < offsets.Count; i++)
            {
                var neighborPoint = Add(sourcePoint, offsets[i]);
                var neighborId = labels.GetRegion(neighborPoint);
                if (!neighborId.HasValue || neighborId.Value != regionBId)
                    continue;

                pairs.Add(new RegionInterfacePair(sourcePoint, neighborPoint));
                boundaryA.Add(sourcePoint);
                boundaryB.Add(neighborPoint);
            }
        }

        pairs.Sort(CompareInterfacePairs);
        return new RegionInterfaceResult
        {
            RegionAId = regionAId,
            RegionAName = regionA.Name,
            RegionBId = regionBId,
            RegionBName = regionB.Name,
            Connectivity = connectivity,
            Pairs = pairs,
            RegionABoundaryVoxels = SortPoints(boundaryA),
            RegionBBoundaryVoxels = SortPoints(boundaryB),
        };
    }

    public double MeasurePointDistance(Point3 pointA, Point3 pointB)
    {
        return Math.Sqrt(CenterDistanceSquared(pointA, pointB));
    }

    public RegionCentroidDistanceResult MeasureRegionCentroidDistance(LabelIndex labels, RegionId regionAId, RegionId regionBId)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var regionA = GetRegion(labels, regionAId);
        var regionB = GetRegion(labels, regionBId);
        var centroidA = CalculateCentroid(regionA);
        var centroidB = CalculateCentroid(regionB);
        var distance = Math.Sqrt(SquaredDistance(centroidA, centroidB));

        return new RegionCentroidDistanceResult
        {
            RegionAId = regionAId,
            RegionAName = regionA.Name,
            RegionACentroid = centroidA,
            RegionBId = regionBId,
            RegionBName = regionB.Name,
            RegionBCentroid = centroidB,
            Distance = distance,
        };
    }

    public RegionNearestDistanceResult MeasureRegionNearestSurfaceDistance(LabelIndex labels, RegionId regionAId, RegionId regionBId)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var regionA = GetRegion(labels, regionAId);
        var regionB = GetRegion(labels, regionBId);
        EnsureRegionHasVoxels(regionA);
        EnsureRegionHasVoxels(regionB);

        bool hasBest = false;
        var bestA = default(Point3);
        var bestB = default(Point3);
        double bestSurfaceSquared = double.MaxValue;
        double bestCenterSquared = double.MaxValue;

        foreach (var pointA in regionA.Voxels)
        {
            foreach (var pointB in regionB.Voxels)
            {
                double surfaceSquared = SurfaceDistanceSquared(pointA, pointB);
                double centerSquared = CenterDistanceSquared(pointA, pointB);
                if (!hasBest || surfaceSquared < bestSurfaceSquared ||
                    (surfaceSquared == bestSurfaceSquared && centerSquared < bestCenterSquared) ||
                    (surfaceSquared == bestSurfaceSquared && centerSquared == bestCenterSquared && ComparePointPair(pointA, pointB, bestA, bestB) < 0))
                {
                    hasBest = true;
                    bestA = pointA;
                    bestB = pointB;
                    bestSurfaceSquared = surfaceSquared;
                    bestCenterSquared = centerSquared;
                }
            }
        }

        return new RegionNearestDistanceResult
        {
            RegionAId = regionAId,
            RegionAName = regionA.Name,
            RegionAVoxel = bestA,
            RegionBId = regionBId,
            RegionBName = regionB.Name,
            RegionBVoxel = bestB,
            VoxelCenterDistance = Math.Sqrt(bestCenterSquared),
            SurfaceDistance = Math.Sqrt(bestSurfaceSquared),
        };
    }

    public CrossSectionResult GetCrossSection(VoxelModel model, LabelIndex labels, CrossSectionAxis axis, int index)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(labels);

        var cells = new List<CrossSectionCell>();
        foreach (var entry in model.Voxels)
        {
            if (GetAxisValue(entry.Key, axis) != index)
                continue;

            var regionId = labels.GetRegion(entry.Key);
            string? regionName = null;
            if (regionId.HasValue && labels.Regions.TryGetValue(regionId.Value, out var region))
                regionName = region.Name;

            cells.Add(new CrossSectionCell
            {
                Position = entry.Key,
                PaletteIndex = entry.Value,
                RegionId = regionId,
                RegionName = regionName,
            });
        }
        cells.Sort(CompareCellsByPoint);

        var bounds = model.GetBounds();
        return new CrossSectionResult
        {
            Axis = axis,
            Index = index,
            UAxis = GetUAxis(axis),
            VAxis = GetVAxis(axis),
            ModelBounds = bounds.HasValue ? new SpatialBox(bounds.Value.Min, bounds.Value.Max) : null,
            Cells = cells,
        };
    }

    public SpatialCollisionResult CheckCollision(LabelIndex labels, SpatialCollisionShape first, SpatialCollisionShape second)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Kind == SpatialCollisionShapeKind.Box && second.Kind == SpatialCollisionShapeKind.Box)
            return CheckBoxBoxCollision(first.Box!.Value, second.Box!.Value);

        if (first.Kind == SpatialCollisionShapeKind.Box && second.Kind == SpatialCollisionShapeKind.Region)
            return CheckBoxRegionCollision(labels, first.Box!.Value, second.RegionId!.Value);

        if (first.Kind == SpatialCollisionShapeKind.Region && second.Kind == SpatialCollisionShapeKind.Box)
            return CheckBoxRegionCollision(labels, second.Box!.Value, first.RegionId!.Value);

        return CheckRegionRegionCollision(labels, first.RegionId!.Value, second.RegionId!.Value);
    }

    public SpatialBox? GetRegionBounds(LabelIndex labels, RegionId regionId)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var region = GetRegion(labels, regionId);
        if (region.Voxels.Count == 0)
            return null;

        bool hasPoint = false;
        var min = default(Point3);
        var max = default(Point3);
        foreach (var point in region.Voxels)
        {
            if (!hasPoint)
            {
                min = point;
                max = point;
                hasPoint = true;
                continue;
            }

            min = new Point3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Point3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        return new SpatialBox(min, max);
    }

    private static SpatialCollisionResult CheckBoxBoxCollision(SpatialBox first, SpatialBox second)
    {
        var intersection = first.Intersect(second);
        return new SpatialCollisionResult
        {
            Collides = intersection.HasValue,
            OverlapVoxelCount = intersection.HasValue ? intersection.Value.VoxelCount() : 0,
            IntersectionBox = intersection,
            OverlapVoxels = [],
        };
    }

    private static SpatialCollisionResult CheckBoxRegionCollision(LabelIndex labels, SpatialBox box, RegionId regionId)
    {
        var region = GetRegion(labels, regionId);
        var overlaps = new List<Point3>();
        foreach (var point in region.Voxels)
        {
            if (box.Contains(point))
                overlaps.Add(point);
        }
        overlaps.Sort(ComparePoints);

        return new SpatialCollisionResult
        {
            Collides = overlaps.Count > 0,
            OverlapVoxelCount = overlaps.Count,
            IntersectionBox = BuildBounds(overlaps),
            OverlapVoxels = overlaps,
        };
    }

    private static SpatialCollisionResult CheckRegionRegionCollision(LabelIndex labels, RegionId firstId, RegionId secondId)
    {
        var first = GetRegion(labels, firstId);
        var second = GetRegion(labels, secondId);
        var overlaps = new List<Point3>();

        if (firstId == secondId)
        {
            overlaps.AddRange(first.Voxels);
        }
        else
        {
            HashSet<Point3> larger;
            HashSet<Point3> smaller;
            if (first.Voxels.Count > second.Voxels.Count)
            {
                larger = first.Voxels;
                smaller = second.Voxels;
            }
            else
            {
                larger = second.Voxels;
                smaller = first.Voxels;
            }

            foreach (var point in smaller)
            {
                if (larger.Contains(point))
                    overlaps.Add(point);
            }
        }
        overlaps.Sort(ComparePoints);

        return new SpatialCollisionResult
        {
            Collides = overlaps.Count > 0,
            OverlapVoxelCount = overlaps.Count,
            IntersectionBox = BuildBounds(overlaps),
            OverlapVoxels = overlaps,
        };
    }

    private static SpatialBox? BuildBounds(IReadOnlyList<Point3> points)
    {
        if (points.Count == 0)
            return null;

        var min = points[0];
        var max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            var point = points[i];
            min = new Point3(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
            max = new Point3(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
        }

        return new SpatialBox(min, max);
    }

    private static RegionDef GetRegion(LabelIndex labels, RegionId regionId)
    {
        if (!labels.Regions.TryGetValue(regionId, out var region))
            throw new InvalidOperationException($"Region '{regionId.Value}' does not exist.");
        return region;
    }

    private static SpatialPointD CalculateCentroid(RegionDef region)
    {
        EnsureRegionHasVoxels(region);
        double x = 0;
        double y = 0;
        double z = 0;
        foreach (var point in region.Voxels)
        {
            x += point.X;
            y += point.Y;
            z += point.Z;
        }

        double count = region.Voxels.Count;
        return new SpatialPointD(x / count, y / count, z / count);
    }

    private static void EnsureRegionHasVoxels(RegionDef region)
    {
        if (region.Voxels.Count == 0)
            throw new InvalidOperationException($"Region '{region.Id.Value}' has no voxels.");
    }

    private static IReadOnlyList<Point3> BuildOffsets(SpatialConnectivity connectivity)
    {
        var offsets = new List<Point3>();
        switch (connectivity)
        {
            case SpatialConnectivity.Six:
                offsets.Add(new Point3(-1, 0, 0));
                offsets.Add(new Point3(1, 0, 0));
                offsets.Add(new Point3(0, -1, 0));
                offsets.Add(new Point3(0, 1, 0));
                offsets.Add(new Point3(0, 0, -1));
                offsets.Add(new Point3(0, 0, 1));
                break;
            case SpatialConnectivity.TwentySix:
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;
                    offsets.Add(new Point3(x, y, z));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(connectivity), connectivity, "Unsupported connectivity.");
        }

        return offsets;
    }

    private static Point3 Add(Point3 point, Point3 offset)
    {
        return new Point3(point.X + offset.X, point.Y + offset.Y, point.Z + offset.Z);
    }

    private static int GetAxisValue(Point3 point, CrossSectionAxis axis)
    {
        return axis switch
        {
            CrossSectionAxis.X => point.X,
            CrossSectionAxis.Y => point.Y,
            CrossSectionAxis.Z => point.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported cross-section axis."),
        };
    }

    private static string GetUAxis(CrossSectionAxis axis)
    {
        return axis switch
        {
            CrossSectionAxis.X => "z",
            CrossSectionAxis.Y => "x",
            CrossSectionAxis.Z => "x",
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported cross-section axis."),
        };
    }

    private static string GetVAxis(CrossSectionAxis axis)
    {
        return axis switch
        {
            CrossSectionAxis.X => "y",
            CrossSectionAxis.Y => "z",
            CrossSectionAxis.Z => "y",
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported cross-section axis."),
        };
    }

    private static double CenterDistanceSquared(Point3 first, Point3 second)
    {
        double x = (double)first.X - second.X;
        double y = (double)first.Y - second.Y;
        double z = (double)first.Z - second.Z;
        return x * x + y * y + z * z;
    }

    private static double SurfaceDistanceSquared(Point3 first, Point3 second)
    {
        double x = Math.Max(0, Math.Abs((double)first.X - second.X) - 1);
        double y = Math.Max(0, Math.Abs((double)first.Y - second.Y) - 1);
        double z = Math.Max(0, Math.Abs((double)first.Z - second.Z) - 1);
        return x * x + y * y + z * z;
    }

    private static double SquaredDistance(SpatialPointD first, SpatialPointD second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        double z = first.Z - second.Z;
        return x * x + y * y + z * z;
    }

    private static List<Point3> SortPoints(IEnumerable<Point3> points)
    {
        var result = new List<Point3>(points);
        result.Sort(ComparePoints);
        return result;
    }

    private static int CompareRegionIds(RegionId left, RegionId right)
    {
        return string.CompareOrdinal(left.Value, right.Value);
    }

    private static int ComparePoints(Point3 left, Point3 right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0) return x;
        int y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : left.Z.CompareTo(right.Z);
    }

    private static int ComparePointPair(Point3 leftA, Point3 leftB, Point3 rightA, Point3 rightB)
    {
        int a = ComparePoints(leftA, rightA);
        return a != 0 ? a : ComparePoints(leftB, rightB);
    }

    private static int CompareInterfacePairs(RegionInterfacePair left, RegionInterfacePair right)
    {
        int a = ComparePoints(left.RegionAVoxel, right.RegionAVoxel);
        return a != 0 ? a : ComparePoints(left.RegionBVoxel, right.RegionBVoxel);
    }

    private static int CompareCellsByPoint(CrossSectionCell left, CrossSectionCell right)
    {
        return ComparePoints(left.Position, right.Position);
    }

    private sealed class NeighborAccumulator
    {
        public NeighborAccumulator(RegionId regionId, string name)
        {
            RegionId = regionId;
            Name = name;
        }

        public RegionId RegionId { get; }

        public string Name { get; }

        public int InterfacePairCount { get; set; }

        public HashSet<Point3> SourceBoundaryVoxels { get; } = [];

        public HashSet<Point3> NeighborBoundaryVoxels { get; } = [];
    }
}
