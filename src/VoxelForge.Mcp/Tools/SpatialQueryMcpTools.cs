using System.Text;
using System.Text.Json;
using VoxelForge.Core;
using VoxelForge.Core.Services;

namespace VoxelForge.Mcp.Tools;

public abstract class SpatialQueryMcpToolBase : RegionMcpToolBase
{
    protected SpatialQueryMcpToolBase(
        VoxelForgeMcpSession session,
        SpatialQueryService spatialQueryService,
        string name,
        string description,
        JsonElement inputSchema)
        : base(session, name, description, inputSchema, isReadOnly: true)
    {
        ArgumentNullException.ThrowIfNull(spatialQueryService);
        SpatialQueryService = spatialQueryService;
    }

    protected SpatialQueryService SpatialQueryService { get; }

    protected static bool TryReadConnectivity(JsonElement arguments, out SpatialConnectivity connectivity, out string errorMessage)
    {
        connectivity = SpatialConnectivity.Six;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("connectivity", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int numericValue))
        {
            if (numericValue == 6)
            {
                connectivity = SpatialConnectivity.Six;
                errorMessage = string.Empty;
                return true;
            }

            if (numericValue == 26)
            {
                connectivity = SpatialConnectivity.TwentySix;
                errorMessage = string.Empty;
                return true;
            }
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString() ?? string.Empty;
            if (string.Equals(text, "6", StringComparison.Ordinal) ||
                string.Equals(text, "six", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "6-connected", StringComparison.OrdinalIgnoreCase))
            {
                connectivity = SpatialConnectivity.Six;
                errorMessage = string.Empty;
                return true;
            }

            if (string.Equals(text, "26", StringComparison.Ordinal) ||
                string.Equals(text, "twenty_six", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "26-connected", StringComparison.OrdinalIgnoreCase))
            {
                connectivity = SpatialConnectivity.TwentySix;
                errorMessage = string.Empty;
                return true;
            }
        }

        errorMessage = "Property 'connectivity' must be 6 or 26 when provided.";
        return false;
    }

    protected static bool TryReadPoint(JsonElement element, string propertyName, out Point3 point, out string errorMessage)
    {
        point = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errorMessage = $"Property '{propertyName}' must be an object.";
            return false;
        }

        if (!TryReadInt(element, "x", out int x, out errorMessage) ||
            !TryReadInt(element, "y", out int y, out errorMessage) ||
            !TryReadInt(element, "z", out int z, out errorMessage))
        {
            errorMessage = PrefixNestedPropertyName(propertyName, errorMessage);
            return false;
        }

        point = new Point3(x, y, z);
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadRequiredPoint(JsonElement arguments, string propertyName, out Point3 point, out string errorMessage)
    {
        point = default;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required object property '{propertyName}'.";
            return false;
        }

        return TryReadPoint(element, propertyName, out point, out errorMessage);
    }

    protected static bool TryReadBox(JsonElement element, string propertyName, out SpatialBox box, out string errorMessage)
    {
        box = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            errorMessage = $"Property '{propertyName}' must be an object.";
            return false;
        }

        if (!TryReadInt(element, "x1", out int x1, out errorMessage) ||
            !TryReadInt(element, "y1", out int y1, out errorMessage) ||
            !TryReadInt(element, "z1", out int z1, out errorMessage) ||
            !TryReadInt(element, "x2", out int x2, out errorMessage) ||
            !TryReadInt(element, "y2", out int y2, out errorMessage) ||
            !TryReadInt(element, "z2", out int z2, out errorMessage))
        {
            errorMessage = PrefixNestedPropertyName(propertyName, errorMessage);
            return false;
        }

        box = SpatialBox.FromCorners(new Point3(x1, y1, z1), new Point3(x2, y2, z2));
        errorMessage = string.Empty;
        return true;
    }

    protected static Dictionary<string, object?> BuildBox(SpatialBox? box)
    {
        if (!box.HasValue)
        {
            return new Dictionary<string, object?>
            {
                ["min"] = null,
                ["max"] = null,
            };
        }

        return new Dictionary<string, object?>
        {
            ["min"] = BuildPoint(box.Value.Min),
            ["max"] = BuildPoint(box.Value.Max),
        };
    }

    protected static List<Dictionary<string, int>> BuildPoints(IReadOnlyList<Point3> points)
    {
        var result = new List<Dictionary<string, int>>();
        for (int i = 0; i < points.Count; i++)
            result.Add(BuildPoint(points[i]));
        return result;
    }

    protected static Dictionary<string, double> BuildPoint(SpatialPointD point)
    {
        return new Dictionary<string, double>
        {
            ["x"] = point.X,
            ["y"] = point.Y,
            ["z"] = point.Z,
        };
    }

    protected static int ConnectivityValue(SpatialConnectivity connectivity)
    {
        return connectivity == SpatialConnectivity.TwentySix ? 26 : 6;
    }

    protected static string AxisText(CrossSectionAxis axis)
    {
        return axis switch
        {
            CrossSectionAxis.X => "x",
            CrossSectionAxis.Y => "y",
            CrossSectionAxis.Z => "z",
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported axis."),
        };
    }

    protected static int AxisValue(Point3 point, string axis)
    {
        return axis switch
        {
            "x" => point.X,
            "y" => point.Y,
            "z" => point.Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unsupported axis."),
        };
    }

    private static string PrefixNestedPropertyName(string propertyName, string errorMessage)
    {
        int firstQuote = errorMessage.IndexOf('\'');
        if (firstQuote < 0)
            return errorMessage;
        int secondQuote = errorMessage.IndexOf('\'', firstQuote + 1);
        if (secondQuote < 0)
            return errorMessage;

        return errorMessage.Substring(0, firstQuote + 1) + propertyName + "." + errorMessage.Substring(firstQuote + 1);
    }
}

public sealed class GetRegionNeighborsMcpTool : SpatialQueryMcpToolBase
{
    public GetRegionNeighborsMcpTool(VoxelForgeMcpSession session, SpatialQueryService spatialQueryService)
        : base(
            session,
            spatialQueryService,
            "get_region_neighbors",
            "Find semantic regions adjacent to a region under 6- or 26-connected voxel adjacency.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_id": { "type": "string" },
                    "connectivity": {
                        "description": "Adjacency mode. Defaults to 6-connected.",
                        "oneOf": [
                            { "type": "integer", "enum": [6, 26] },
                            { "type": "string", "enum": ["6", "26", "six", "twenty_six", "6-connected", "26-connected"] }
                        ]
                    }
                },
                "required": ["region_id"]
            }
            """))
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_id", out var regionIdText, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadConnectivity(arguments, out var connectivity, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = SpatialQueryService.GetRegionNeighbors(Session.Document.Labels, new RegionId(regionIdText), connectivity);
                var neighbors = new List<Dictionary<string, object?>>();
                for (int i = 0; i < result.Neighbors.Count; i++)
                {
                    var neighbor = result.Neighbors[i];
                    neighbors.Add(new Dictionary<string, object?>
                    {
                        ["regionId"] = neighbor.RegionId.Value,
                        ["name"] = neighbor.Name,
                        ["interfacePairCount"] = neighbor.InterfacePairCount,
                        ["sourceBoundaryVoxelCount"] = neighbor.SourceBoundaryVoxels.Count,
                        ["neighborBoundaryVoxelCount"] = neighbor.NeighborBoundaryVoxels.Count,
                        ["sourceBoundaryVoxels"] = BuildPoints(neighbor.SourceBoundaryVoxels),
                        ["neighborBoundaryVoxels"] = BuildPoints(neighbor.NeighborBoundaryVoxels),
                    });
                }

                return Ok(SerializeJson(new Dictionary<string, object?>
                {
                    ["regionId"] = result.RegionId.Value,
                    ["name"] = result.Name,
                    ["connectivity"] = ConnectivityValue(result.Connectivity),
                    ["neighbors"] = neighbors,
                    ["count"] = neighbors.Count,
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }
    }
}

public sealed class GetRegionNeighborsServerTool : VoxelForgeMcpServerTool
{
    public GetRegionNeighborsServerTool(GetRegionNeighborsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetInterfaceVoxelsMcpTool : SpatialQueryMcpToolBase
{
    public GetInterfaceVoxelsMcpTool(VoxelForgeMcpSession session, SpatialQueryService spatialQueryService)
        : base(
            session,
            spatialQueryService,
            "get_interface_voxels",
            "Return voxel pairs and boundary voxels where two semantic regions touch under 6- or 26-connected adjacency.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_a": { "type": "string" },
                    "region_b": { "type": "string" },
                    "connectivity": {
                        "description": "Adjacency mode. Defaults to 6-connected.",
                        "oneOf": [
                            { "type": "integer", "enum": [6, 26] },
                            { "type": "string", "enum": ["6", "26", "six", "twenty_six", "6-connected", "26-connected"] }
                        ]
                    }
                },
                "required": ["region_a", "region_b"]
            }
            """))
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_a", out var regionAText, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadRequiredString(arguments, "region_b", out var regionBText, out errorMessage))
            return Fail(errorMessage);
        if (string.Equals(regionAText, regionBText, StringComparison.Ordinal))
            return Fail("Properties 'region_a' and 'region_b' must refer to different regions.");
        if (!TryReadConnectivity(arguments, out var connectivity, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = SpatialQueryService.GetInterfaceVoxels(
                    Session.Document.Labels,
                    new RegionId(regionAText),
                    new RegionId(regionBText),
                    connectivity);
                var pairs = new List<Dictionary<string, object?>>();
                for (int i = 0; i < result.Pairs.Count; i++)
                {
                    pairs.Add(new Dictionary<string, object?>
                    {
                        ["a"] = BuildPoint(result.Pairs[i].RegionAVoxel),
                        ["b"] = BuildPoint(result.Pairs[i].RegionBVoxel),
                    });
                }

                return Ok(SerializeJson(new Dictionary<string, object?>
                {
                    ["regionA"] = new Dictionary<string, object?>
                    {
                        ["id"] = result.RegionAId.Value,
                        ["name"] = result.RegionAName,
                    },
                    ["regionB"] = new Dictionary<string, object?>
                    {
                        ["id"] = result.RegionBId.Value,
                        ["name"] = result.RegionBName,
                    },
                    ["connectivity"] = ConnectivityValue(result.Connectivity),
                    ["interfacePairCount"] = result.Pairs.Count,
                    ["regionABoundaryVoxelCount"] = result.RegionABoundaryVoxels.Count,
                    ["regionBBoundaryVoxelCount"] = result.RegionBBoundaryVoxels.Count,
                    ["regionABoundaryVoxels"] = BuildPoints(result.RegionABoundaryVoxels),
                    ["regionBBoundaryVoxels"] = BuildPoints(result.RegionBBoundaryVoxels),
                    ["pairs"] = pairs,
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }
    }
}

public sealed class GetInterfaceVoxelsServerTool : VoxelForgeMcpServerTool
{
    public GetInterfaceVoxelsServerTool(GetInterfaceVoxelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class MeasureDistanceMcpTool : SpatialQueryMcpToolBase
{
    public MeasureDistanceMcpTool(VoxelForgeMcpSession session, SpatialQueryService spatialQueryService)
        : base(
            session,
            spatialQueryService,
            "measure_distance",
            "Measure Euclidean distance between two points, or centroid/nearest-surface distance between two semantic regions.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "point_a": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "integer" },
                            "y": { "type": "integer" },
                            "z": { "type": "integer" }
                        },
                        "required": ["x", "y", "z"]
                    },
                    "point_b": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "integer" },
                            "y": { "type": "integer" },
                            "z": { "type": "integer" }
                        },
                        "required": ["x", "y", "z"]
                    },
                    "region_a": { "type": "string" },
                    "region_b": { "type": "string" },
                    "mode": {
                        "type": "string",
                        "enum": ["centroid", "nearest_surface"],
                        "description": "Region distance mode. Defaults to nearest_surface for region measurements."
                    }
                },
                "oneOf": [
                    { "required": ["point_a", "point_b"] },
                    { "required": ["region_a", "region_b"] }
                ]
            }
            """))
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JsonElement pointAElement = default;
        JsonElement pointBElement = default;
        JsonElement regionAElement = default;
        JsonElement regionBElement = default;
        bool hasPointA = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("point_a", out pointAElement) && pointAElement.ValueKind != JsonValueKind.Null;
        bool hasPointB = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("point_b", out pointBElement) && pointBElement.ValueKind != JsonValueKind.Null;
        bool hasRegionA = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("region_a", out regionAElement) && regionAElement.ValueKind != JsonValueKind.Null;
        bool hasRegionB = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("region_b", out regionBElement) && regionBElement.ValueKind != JsonValueKind.Null;

        if (hasPointA || hasPointB)
        {
            if (!hasPointA || !hasPointB || hasRegionA || hasRegionB)
                return Fail("Provide either point_a and point_b, or region_a and region_b.");
            if (!TryReadRequiredPoint(arguments, "point_a", out var pointA, out var errorMessage))
                return Fail(errorMessage);
            if (!TryReadRequiredPoint(arguments, "point_b", out var pointB, out errorMessage))
                return Fail(errorMessage);

            var distance = SpatialQueryService.MeasurePointDistance(pointA, pointB);
            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["mode"] = "point",
                ["unit"] = "voxels",
                ["distance"] = distance,
                ["pointA"] = BuildPoint(pointA),
                ["pointB"] = BuildPoint(pointB),
            }));
        }

        if (!hasRegionA || !hasRegionB)
            return Fail("Provide either point_a and point_b, or region_a and region_b.");
        if (regionAElement.ValueKind != JsonValueKind.String || regionBElement.ValueKind != JsonValueKind.String)
            return Fail("Properties 'region_a' and 'region_b' must be strings.");

        var regionA = regionAElement.GetString() ?? string.Empty;
        var regionB = regionBElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(regionA) || string.IsNullOrWhiteSpace(regionB))
            return Fail("Properties 'region_a' and 'region_b' cannot be empty.");
        if (string.Equals(regionA, regionB, StringComparison.Ordinal))
            return Fail("Properties 'region_a' and 'region_b' must refer to different regions.");
        if (!TryReadDistanceMode(arguments, out var mode, out var modeError))
            return Fail(modeError);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (string.Equals(mode, "centroid", StringComparison.Ordinal))
                {
                    var result = SpatialQueryService.MeasureRegionCentroidDistance(Session.Document.Labels, new RegionId(regionA), new RegionId(regionB));
                    return Ok(SerializeJson(new Dictionary<string, object?>
                    {
                        ["mode"] = "centroid",
                        ["unit"] = "voxels",
                        ["distance"] = result.Distance,
                        ["regionA"] = new Dictionary<string, object?>
                        {
                            ["id"] = result.RegionAId.Value,
                            ["name"] = result.RegionAName,
                            ["centroid"] = BuildPoint(result.RegionACentroid),
                        },
                        ["regionB"] = new Dictionary<string, object?>
                        {
                            ["id"] = result.RegionBId.Value,
                            ["name"] = result.RegionBName,
                            ["centroid"] = BuildPoint(result.RegionBCentroid),
                        },
                    }));
                }

                var nearest = SpatialQueryService.MeasureRegionNearestSurfaceDistance(Session.Document.Labels, new RegionId(regionA), new RegionId(regionB));
                return Ok(SerializeJson(new Dictionary<string, object?>
                {
                    ["mode"] = "nearest_surface",
                    ["unit"] = "voxels",
                    ["distance"] = nearest.SurfaceDistance,
                    ["voxelCenterDistance"] = nearest.VoxelCenterDistance,
                    ["regionA"] = new Dictionary<string, object?>
                    {
                        ["id"] = nearest.RegionAId.Value,
                        ["name"] = nearest.RegionAName,
                        ["voxel"] = BuildPoint(nearest.RegionAVoxel),
                    },
                    ["regionB"] = new Dictionary<string, object?>
                    {
                        ["id"] = nearest.RegionBId.Value,
                        ["name"] = nearest.RegionBName,
                        ["voxel"] = BuildPoint(nearest.RegionBVoxel),
                    },
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }
    }

    private static bool TryReadDistanceMode(JsonElement arguments, out string mode, out string errorMessage)
    {
        mode = "nearest_surface";
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("mode", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = "Property 'mode' must be a string when provided.";
            return false;
        }

        var text = element.GetString() ?? string.Empty;
        if (string.Equals(text, "centroid", StringComparison.Ordinal) || string.Equals(text, "nearest_surface", StringComparison.Ordinal))
        {
            mode = text;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = "Property 'mode' must be 'centroid' or 'nearest_surface'.";
        return false;
    }
}

public sealed class MeasureDistanceServerTool : VoxelForgeMcpServerTool
{
    public MeasureDistanceServerTool(MeasureDistanceMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetCrossSectionMcpTool : SpatialQueryMcpToolBase
{
    private const string RegionValueMode = "region";
    private const string PaletteValueMode = "palette";
    private const string RegionSymbols = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string PaletteSymbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public GetCrossSectionMcpTool(VoxelForgeMcpSession session, SpatialQueryService spatialQueryService)
        : base(
            session,
            spatialQueryService,
            "get_cross_section",
            "Slice the model along X=n, Y=n, or Z=n and return a compact 2D text grid plus legend.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "axis": { "type": "string", "enum": ["x", "y", "z"] },
                    "index": { "type": "integer" },
                    "value_mode": {
                        "type": "string",
                        "enum": ["region", "palette"],
                        "description": "Use region symbols or palette-index symbols. Defaults to region."
                    }
                },
                "required": ["axis", "index"]
            }
            """))
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadAxis(arguments, out var axis, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadInt(arguments, "index", out int index, out errorMessage))
            return Fail(errorMessage);
        if (!TryReadValueMode(arguments, out var valueMode, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = SpatialQueryService.GetCrossSection(Session.Document.Model, Session.Document.Labels, axis, index);
            var rendered = BuildRenderedCrossSection(Session.Document.Model, result, valueMode);
            return Ok(SerializeJson(rendered));
        }
    }

    private static bool TryReadAxis(JsonElement arguments, out CrossSectionAxis axis, out string errorMessage)
    {
        axis = CrossSectionAxis.Z;
        if (!TryReadRequiredString(arguments, "axis", out var text, out errorMessage))
            return false;

        if (string.Equals(text, "x", StringComparison.OrdinalIgnoreCase))
        {
            axis = CrossSectionAxis.X;
            errorMessage = string.Empty;
            return true;
        }

        if (string.Equals(text, "y", StringComparison.OrdinalIgnoreCase))
        {
            axis = CrossSectionAxis.Y;
            errorMessage = string.Empty;
            return true;
        }

        if (string.Equals(text, "z", StringComparison.OrdinalIgnoreCase))
        {
            axis = CrossSectionAxis.Z;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = "Property 'axis' must be 'x', 'y', or 'z'.";
        return false;
    }

    private static bool TryReadValueMode(JsonElement arguments, out string valueMode, out string errorMessage)
    {
        valueMode = RegionValueMode;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("value_mode", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = "Property 'value_mode' must be a string when provided.";
            return false;
        }

        var text = element.GetString() ?? string.Empty;
        if (string.Equals(text, RegionValueMode, StringComparison.Ordinal) || string.Equals(text, PaletteValueMode, StringComparison.Ordinal))
        {
            valueMode = text;
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = "Property 'value_mode' must be 'region' or 'palette'.";
        return false;
    }

    private static Dictionary<string, object?> BuildRenderedCrossSection(VoxelModel model, CrossSectionResult result, string valueMode)
    {
        if (!result.ModelBounds.HasValue)
        {
            return new Dictionary<string, object?>
            {
                ["axis"] = AxisText(result.Axis),
                ["index"] = result.Index,
                ["uAxis"] = result.UAxis,
                ["vAxis"] = result.VAxis,
                ["valueMode"] = valueMode,
                ["modelBounds"] = null,
                ["uRange"] = null,
                ["vRange"] = null,
                ["occupiedCount"] = 0,
                ["rows"] = Array.Empty<string>(),
                ["text"] = string.Empty,
                ["legend"] = Array.Empty<Dictionary<string, object?>>(),
            };
        }

        var bounds = result.ModelBounds.Value;
        int minU = AxisValue(bounds.Min, result.UAxis);
        int maxU = AxisValue(bounds.Max, result.UAxis);
        int minV = AxisValue(bounds.Min, result.VAxis);
        int maxV = AxisValue(bounds.Max, result.VAxis);
        var cellsByCoordinate = new Dictionary<GridPoint2, CrossSectionCell>();
        for (int i = 0; i < result.Cells.Count; i++)
        {
            var cell = result.Cells[i];
            cellsByCoordinate[new GridPoint2(AxisValue(cell.Position, result.UAxis), AxisValue(cell.Position, result.VAxis))] = cell;
        }

        var symbolContext = valueMode == RegionValueMode
            ? BuildRegionSymbolContext(result.Cells)
            : BuildPaletteSymbolContext(model, result.Cells);

        var rows = new List<string>();
        for (int v = maxV; v >= minV; v--)
        {
            var builder = new StringBuilder();
            for (int u = minU; u <= maxU; u++)
            {
                if (cellsByCoordinate.TryGetValue(new GridPoint2(u, v), out var cell))
                {
                    var symbol = valueMode == RegionValueMode
                        ? GetRegionCellSymbol(symbolContext, cell)
                        : GetPaletteCellSymbol(symbolContext, cell);
                    IncrementSymbolCount(symbolContext, symbol);
                    builder.Append(symbol);
                }
                else
                {
                    builder.Append('.');
                }
            }
            rows.Add(builder.ToString());
        }

        var legend = BuildLegend(symbolContext, valueMode);
        return new Dictionary<string, object?>
        {
            ["axis"] = AxisText(result.Axis),
            ["index"] = result.Index,
            ["uAxis"] = result.UAxis,
            ["vAxis"] = result.VAxis,
            ["valueMode"] = valueMode,
            ["modelBounds"] = BuildBox(result.ModelBounds),
            ["uRange"] = new Dictionary<string, int> { ["min"] = minU, ["max"] = maxU },
            ["vRange"] = new Dictionary<string, int> { ["min"] = minV, ["max"] = maxV },
            ["occupiedCount"] = result.Cells.Count,
            ["rows"] = rows,
            ["text"] = string.Join("\n", rows),
            ["legend"] = legend,
        };
    }

    private static SymbolContext BuildRegionSymbolContext(IReadOnlyList<CrossSectionCell> cells)
    {
        var regionIds = new List<RegionId>();
        var seen = new HashSet<RegionId>();
        var context = new SymbolContext();
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].RegionId.HasValue)
            {
                var regionId = cells[i].RegionId!.Value;
                context.RegionNames[regionId] = cells[i].RegionName ?? regionId.Value;
                if (seen.Add(regionId))
                    regionIds.Add(regionId);
            }
        }
        regionIds.Sort(CompareRegionIds);

        for (int i = 0; i < regionIds.Count; i++)
        {
            var symbol = SymbolAt(RegionSymbols, i);
            context.RegionSymbols[regionIds[i]] = symbol;
        }

        return context;
    }

    private static SymbolContext BuildPaletteSymbolContext(VoxelModel model, IReadOnlyList<CrossSectionCell> cells)
    {
        var paletteIndices = new List<byte>();
        var seen = new HashSet<byte>();
        for (int i = 0; i < cells.Count; i++)
        {
            if (seen.Add(cells[i].PaletteIndex))
                paletteIndices.Add(cells[i].PaletteIndex);
        }
        paletteIndices.Sort();

        var context = new SymbolContext();
        for (int i = 0; i < paletteIndices.Count; i++)
        {
            var paletteIndex = paletteIndices[i];
            var symbol = paletteIndex < PaletteSymbols.Length ? PaletteSymbols[paletteIndex].ToString() : SymbolAt(PaletteSymbols, i);
            context.PaletteSymbols[paletteIndex] = symbol;
            context.PaletteNames[paletteIndex] = model.Palette.Get(paletteIndex)?.Name ?? "unknown";
        }

        return context;
    }

    private static string GetRegionCellSymbol(SymbolContext context, CrossSectionCell cell)
    {
        if (!cell.RegionId.HasValue)
            return "#";
        return context.RegionSymbols.TryGetValue(cell.RegionId.Value, out var symbol) ? symbol : "?";
    }

    private static string GetPaletteCellSymbol(SymbolContext context, CrossSectionCell cell)
    {
        return context.PaletteSymbols.TryGetValue(cell.PaletteIndex, out var symbol) ? symbol : "?";
    }

    private static string SymbolAt(string symbols, int index)
    {
        return index >= 0 && index < symbols.Length ? symbols[index].ToString() : "?";
    }

    private static void IncrementSymbolCount(SymbolContext context, string symbol)
    {
        context.SymbolCounts.TryGetValue(symbol, out int count);
        context.SymbolCounts[symbol] = count + 1;
    }

    private static List<Dictionary<string, object?>> BuildLegend(SymbolContext context, string valueMode)
    {
        var legend = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["symbol"] = ".",
                ["label"] = "air",
            },
        };

        if (valueMode == RegionValueMode)
        {
            var regionEntries = new List<KeyValuePair<RegionId, string>>(context.RegionSymbols);
            regionEntries.Sort(CompareRegionSymbolEntries);
            for (int i = 0; i < regionEntries.Count; i++)
            {
                context.SymbolCounts.TryGetValue(regionEntries[i].Value, out int count);
                context.RegionNames.TryGetValue(regionEntries[i].Key, out var regionName);
                legend.Add(new Dictionary<string, object?>
                {
                    ["symbol"] = regionEntries[i].Value,
                    ["regionId"] = regionEntries[i].Key.Value,
                    ["name"] = regionName ?? regionEntries[i].Key.Value,
                    ["cellCount"] = count,
                });
            }

            if (context.SymbolCounts.TryGetValue("#", out int unlabeledCount))
            {
                legend.Add(new Dictionary<string, object?>
                {
                    ["symbol"] = "#",
                    ["label"] = "unlabeled occupied voxel",
                    ["cellCount"] = unlabeledCount,
                });
            }
        }
        else
        {
            var paletteEntries = new List<KeyValuePair<byte, string>>(context.PaletteSymbols);
            paletteEntries.Sort(ComparePaletteSymbolEntries);
            for (int i = 0; i < paletteEntries.Count; i++)
            {
                context.SymbolCounts.TryGetValue(paletteEntries[i].Value, out int count);
                context.PaletteNames.TryGetValue(paletteEntries[i].Key, out var materialName);
                legend.Add(new Dictionary<string, object?>
                {
                    ["symbol"] = paletteEntries[i].Value,
                    ["paletteIndex"] = paletteEntries[i].Key,
                    ["materialName"] = materialName ?? "unknown",
                    ["cellCount"] = count,
                });
            }
        }

        return legend;
    }

    private static int CompareRegionIds(RegionId left, RegionId right)
    {
        return string.CompareOrdinal(left.Value, right.Value);
    }

    private static int CompareRegionSymbolEntries(KeyValuePair<RegionId, string> left, KeyValuePair<RegionId, string> right)
    {
        return CompareRegionIds(left.Key, right.Key);
    }

    private static int ComparePaletteSymbolEntries(KeyValuePair<byte, string> left, KeyValuePair<byte, string> right)
    {
        return left.Key.CompareTo(right.Key);
    }

    private readonly record struct GridPoint2(int U, int V);

    private sealed class SymbolContext
    {
        public Dictionary<RegionId, string> RegionSymbols { get; } = [];

        public Dictionary<RegionId, string> RegionNames { get; } = [];

        public Dictionary<byte, string> PaletteSymbols { get; } = [];

        public Dictionary<byte, string> PaletteNames { get; } = [];

        public Dictionary<string, int> SymbolCounts { get; } = new(StringComparer.Ordinal);
    }
}

public sealed class GetCrossSectionServerTool : VoxelForgeMcpServerTool
{
    public GetCrossSectionServerTool(GetCrossSectionMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class CheckCollisionMcpTool : SpatialQueryMcpToolBase
{
    public CheckCollisionMcpTool(VoxelForgeMcpSession session, SpatialQueryService spatialQueryService)
        : base(
            session,
            spatialQueryService,
            "check_collision",
            "Check whether two semantic regions or inclusive bounding boxes overlap in occupied voxel coordinates.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "a": { "$ref": "#/$defs/shape" },
                    "b": { "$ref": "#/$defs/shape" }
                },
                "required": ["a", "b"],
                "$defs": {
                    "box": {
                        "type": "object",
                        "properties": {
                            "x1": { "type": "integer" },
                            "y1": { "type": "integer" },
                            "z1": { "type": "integer" },
                            "x2": { "type": "integer" },
                            "y2": { "type": "integer" },
                            "z2": { "type": "integer" }
                        },
                        "required": ["x1", "y1", "z1", "x2", "y2", "z2"]
                    },
                    "shape": {
                        "type": "object",
                        "properties": {
                            "region_id": { "type": "string" },
                            "box": { "$ref": "#/$defs/box" }
                        },
                        "oneOf": [
                            { "required": ["region_id"] },
                            { "required": ["box"] }
                        ]
                    }
                }
            }
            """))
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadShape(arguments, "a", out var first, out var firstSummary, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadShape(arguments, "b", out var second, out var secondSummary, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = SpatialQueryService.CheckCollision(Session.Document.Labels, first, second);
                return Ok(SerializeJson(new Dictionary<string, object?>
                {
                    ["collides"] = result.Collides,
                    ["overlapVoxelCount"] = result.OverlapVoxelCount,
                    ["intersectionBox"] = result.IntersectionBox.HasValue ? BuildBox(result.IntersectionBox) : null,
                    ["overlapVoxels"] = BuildPoints(result.OverlapVoxels),
                    ["a"] = firstSummary,
                    ["b"] = secondSummary,
                }));
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
        }
    }

    private static bool TryReadShape(
        JsonElement arguments,
        string propertyName,
        out SpatialCollisionShape shape,
        out Dictionary<string, object?> summary,
        out string errorMessage)
    {
        shape = SpatialCollisionShape.FromBox(SpatialBox.FromCorners(default, default));
        summary = [];
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required object property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errorMessage = $"Property '{propertyName}' must be an object.";
            return false;
        }

        bool hasRegion = element.TryGetProperty("region_id", out var regionElement) && regionElement.ValueKind != JsonValueKind.Null;
        bool hasBox = element.TryGetProperty("box", out var boxElement) && boxElement.ValueKind != JsonValueKind.Null;
        if (hasRegion == hasBox)
        {
            errorMessage = $"Property '{propertyName}' must contain exactly one of 'region_id' or 'box'.";
            return false;
        }

        if (hasRegion)
        {
            if (regionElement.ValueKind != JsonValueKind.String)
            {
                errorMessage = $"Property '{propertyName}.region_id' must be a string.";
                return false;
            }

            var regionId = regionElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(regionId))
            {
                errorMessage = $"Property '{propertyName}.region_id' cannot be empty.";
                return false;
            }

            shape = SpatialCollisionShape.FromRegion(new RegionId(regionId));
            summary = new Dictionary<string, object?>
            {
                ["kind"] = "region",
                ["regionId"] = regionId,
            };
            errorMessage = string.Empty;
            return true;
        }

        if (!TryReadBox(boxElement, propertyName + ".box", out var box, out errorMessage))
            return false;

        shape = SpatialCollisionShape.FromBox(box);
        summary = new Dictionary<string, object?>
        {
            ["kind"] = "box",
            ["bounds"] = BuildBox(box),
        };
        errorMessage = string.Empty;
        return true;
    }
}

public sealed class CheckCollisionServerTool : VoxelForgeMcpServerTool
{
    public CheckCollisionServerTool(CheckCollisionMcpTool tool)
        : base(tool)
    {
    }
}
