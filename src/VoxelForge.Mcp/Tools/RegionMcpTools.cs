using System.Text.Json;
using VoxelForge.App.Services;
using VoxelForge.Core;

namespace VoxelForge.Mcp.Tools;

public abstract class RegionMcpToolBase : IVoxelForgeMcpTool
{
    private readonly JsonElement _inputSchema;

    protected RegionMcpToolBase(VoxelForgeMcpSession session, string name, string description, JsonElement inputSchema, bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Session = session;
        Name = name;
        Description = description;
        _inputSchema = inputSchema;
        IsReadOnly = isReadOnly;
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement InputSchema => _inputSchema;

    public bool IsReadOnly { get; }

    protected VoxelForgeMcpSession Session { get; }

    public abstract McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken);

    protected static bool TryReadRequiredString(JsonElement arguments, string propertyName, out string value, out string errorMessage)
    {
        value = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required string property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Property '{propertyName}' must be a string.";
            return false;
        }

        value = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = $"Property '{propertyName}' cannot be empty.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadOptionalString(JsonElement arguments, string propertyName, out string? value, out string errorMessage)
    {
        value = null;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errorMessage = $"Property '{propertyName}' must be a string when provided.";
            return false;
        }

        value = element.GetString();
        errorMessage = string.Empty;
        return true;
    }

    protected static bool TryReadInt(JsonElement arguments, string propertyName, out int value, out string errorMessage)
    {
        value = 0;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var element))
        {
            errorMessage = $"Missing required integer property '{propertyName}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out value))
        {
            errorMessage = $"Property '{propertyName}' must be an integer.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    protected static McpToolInvocationResult Fail(string message)
    {
        return new McpToolInvocationResult
        {
            Success = false,
            Message = message,
        };
    }

    protected static McpToolInvocationResult Ok(string message)
    {
        return new McpToolInvocationResult
        {
            Success = true,
            Message = message,
        };
    }

    protected static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    protected static Dictionary<string, object?> BuildRegionSummary(LabelIndex labels, RegionId regionId, RegionDef region)
    {
        var childIds = new List<string>();
        foreach (var entry in labels.Regions)
        {
            if (entry.Value.ParentId == regionId)
                childIds.Add(entry.Key.Value);
        }
        childIds.Sort(StringComparer.Ordinal);

        var ancestors = labels.GetAncestors(regionId);
        var ancestorIds = new List<string>();
        for (int i = 0; i < ancestors.Count; i++)
        {
            if (ancestors[i] != regionId)
                ancestorIds.Add(ancestors[i].Value);
        }

        var descendants = labels.GetDescendants(regionId);
        var descendantIds = new List<string>();
        for (int i = 0; i < descendants.Count; i++)
            descendantIds.Add(descendants[i].Value);
        descendantIds.Sort(StringComparer.Ordinal);

        return new Dictionary<string, object?>
        {
            ["id"] = regionId.Value,
            ["name"] = region.Name,
            ["voxelCount"] = region.Voxels.Count,
            ["parentId"] = region.ParentId?.Value,
            ["childIds"] = childIds,
            ["ancestorIds"] = ancestorIds,
            ["descendantIds"] = descendantIds,
            ["bounds"] = BuildBounds(region),
            ["properties"] = new Dictionary<string, string>(region.Properties, StringComparer.Ordinal),
        };
    }

    protected static Dictionary<string, object?>? BuildBounds(RegionDef region)
    {
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

        return new Dictionary<string, object?>
        {
            ["min"] = BuildPoint(min),
            ["max"] = BuildPoint(max),
        };
    }

    protected static Dictionary<string, int> BuildPoint(Point3 point)
    {
        return new Dictionary<string, int>
        {
            ["x"] = point.X,
            ["y"] = point.Y,
            ["z"] = point.Z,
        };
    }

    protected static int ComparePoints(Point3 left, Point3 right)
    {
        int x = left.X.CompareTo(right.X);
        if (x != 0) return x;
        int y = left.Y.CompareTo(right.Y);
        return y != 0 ? y : left.Z.CompareTo(right.Z);
    }
}

public sealed class ListRegionsMcpTool : RegionMcpToolBase
{
    public ListRegionsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "list_regions",
            "List all semantic regions with voxel counts, parent hierarchy, properties, and bounding boxes.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regions = new List<Dictionary<string, object?>>();
            foreach (var entry in Session.Document.Labels.Regions)
                regions.Add(BuildRegionSummary(Session.Document.Labels, entry.Key, entry.Value));

            regions.Sort(CompareRegionSummaries);
            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["regions"] = regions,
                ["count"] = regions.Count,
            }));
        }
    }

    private static int CompareRegionSummaries(Dictionary<string, object?> left, Dictionary<string, object?> right)
    {
        return string.CompareOrdinal((string?)left["id"], (string?)right["id"]);
    }
}

public sealed class ListRegionsServerTool : VoxelForgeMcpServerTool
{
    public ListRegionsServerTool(ListRegionsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class CreateRegionMcpTool : RegionMcpToolBase
{
    private readonly RegionEditingService _regionEditingService;

    public CreateRegionMcpTool(VoxelForgeMcpSession session, RegionEditingService regionEditingService)
        : base(
            session,
            "create_region",
            "Create a named semantic region with optional parent id and string properties.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string", "description": "Region id and display name." },
                    "parent_id": { "type": "string", "description": "Optional parent region id." },
                    "properties": {
                        "type": "object",
                        "additionalProperties": { "type": "string" },
                        "description": "Optional string metadata dictionary."
                    }
                },
                "required": ["name"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(regionEditingService);
        _regionEditingService = regionEditingService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "name", out var name, out var errorMessage))
            return Fail(errorMessage);
        if (!TryReadOptionalString(arguments, "parent_id", out var parentIdText, out errorMessage))
            return Fail(errorMessage);
        if (!TryReadProperties(arguments, out var properties, out errorMessage))
            return Fail(errorMessage);

        var request = new CreateRegionRequest(
            name,
            string.IsNullOrWhiteSpace(parentIdText) ? null : new RegionId(parentIdText),
            properties);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _regionEditingService.CreateRegion(
                Session.Document.Labels,
                Session.UndoStack,
                Session.Events,
                request);
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }

    private static bool TryReadProperties(JsonElement arguments, out IReadOnlyDictionary<string, string> properties, out string errorMessage)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        properties = result;
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("properties", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errorMessage = string.Empty;
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Property 'properties' must be an object when provided.";
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                errorMessage = $"Property 'properties.{property.Name}' must be a string.";
                return false;
            }

            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        errorMessage = string.Empty;
        return true;
    }
}

public sealed class CreateRegionServerTool : VoxelForgeMcpServerTool
{
    public CreateRegionServerTool(CreateRegionMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class DeleteRegionMcpTool : RegionMcpToolBase
{
    private readonly RegionEditingService _regionEditingService;

    public DeleteRegionMcpTool(VoxelForgeMcpSession session, RegionEditingService regionEditingService)
        : base(
            session,
            "delete_region",
            "Delete a semantic region definition and unlabel its voxels without removing voxels from the model.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_id": { "type": "string" }
                },
                "required": ["region_id"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(regionEditingService);
        _regionEditingService = regionEditingService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_id", out var regionId, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _regionEditingService.DeleteRegion(
                Session.Document.Labels,
                Session.UndoStack,
                Session.Events,
                new DeleteRegionRequest(regionId));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }
}

public sealed class DeleteRegionServerTool : VoxelForgeMcpServerTool
{
    public DeleteRegionServerTool(DeleteRegionMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class AssignVoxelsToRegionMcpTool : RegionMcpToolBase
{
    private readonly RegionEditingService _regionEditingService;

    public AssignVoxelsToRegionMcpTool(VoxelForgeMcpSession session, RegionEditingService regionEditingService)
        : base(
            session,
            "assign_voxels_to_region",
            "Assign existing model voxels to a semantic region by coordinate list or inclusive bounding box.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_id": { "type": "string" },
                    "positions": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "x": { "type": "integer" },
                                "y": { "type": "integer" },
                                "z": { "type": "integer" }
                            },
                            "required": ["x", "y", "z"]
                        }
                    },
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
                    }
                },
                "required": ["region_id"],
                "oneOf": [
                    { "required": ["positions"] },
                    { "required": ["box"] }
                ]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(regionEditingService);
        _regionEditingService = regionEditingService;
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_id", out var regionId, out var errorMessage))
            return Fail(errorMessage);

        JsonElement positionsElement = default;
        JsonElement boxElement = default;
        bool hasPositions = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("positions", out positionsElement) && positionsElement.ValueKind != JsonValueKind.Null;
        bool hasBox = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("box", out boxElement) && boxElement.ValueKind != JsonValueKind.Null;
        if (hasPositions == hasBox)
            return Fail("Provide exactly one of 'positions' or 'box'.");

        IReadOnlyList<Point3> explicitPositions = [];
        if (hasPositions && !TryReadPositions(positionsElement, out explicitPositions, out errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var positions = hasBox
                ? CollectBoxPositions(Session.Document.Model, boxElement, out errorMessage)
                : explicitPositions;
            if (positions is null)
                return Fail(errorMessage);

            var result = _regionEditingService.AssignVoxels(
                Session.Document,
                Session.UndoStack,
                Session.Events,
                new AssignVoxelsRegionRequest(regionId, positions));
            return new McpToolInvocationResult
            {
                Success = result.Success,
                Message = result.Message,
            };
        }
    }

    private static bool TryReadPositions(JsonElement element, out IReadOnlyList<Point3> positions, out string errorMessage)
    {
        var result = new List<Point3>();
        var seen = new HashSet<Point3>();
        positions = result;
        if (element.ValueKind != JsonValueKind.Array)
        {
            errorMessage = "Property 'positions' must be an array when provided.";
            return false;
        }

        foreach (var positionElement in element.EnumerateArray())
        {
            if (!TryReadInt(positionElement, "x", out int x, out errorMessage) ||
                !TryReadInt(positionElement, "y", out int y, out errorMessage) ||
                !TryReadInt(positionElement, "z", out int z, out errorMessage))
            {
                return false;
            }

            var point = new Point3(x, y, z);
            if (seen.Add(point))
                result.Add(point);
        }

        errorMessage = string.Empty;
        return true;
    }

    private static IReadOnlyList<Point3>? CollectBoxPositions(VoxelModel model, JsonElement boxElement, out string errorMessage)
    {
        if (boxElement.ValueKind != JsonValueKind.Object)
        {
            errorMessage = "Property 'box' must be an object when provided.";
            return null;
        }

        if (!TryReadInt(boxElement, "x1", out int x1, out errorMessage) ||
            !TryReadInt(boxElement, "y1", out int y1, out errorMessage) ||
            !TryReadInt(boxElement, "z1", out int z1, out errorMessage) ||
            !TryReadInt(boxElement, "x2", out int x2, out errorMessage) ||
            !TryReadInt(boxElement, "y2", out int y2, out errorMessage) ||
            !TryReadInt(boxElement, "z2", out int z2, out errorMessage))
        {
            return null;
        }

        var min = new Point3(Math.Min(x1, x2), Math.Min(y1, y2), Math.Min(z1, z2));
        var max = new Point3(Math.Max(x1, x2), Math.Max(y1, y2), Math.Max(z1, z2));
        var result = new List<Point3>();
        foreach (var entry in model.Voxels)
        {
            var point = entry.Key;
            if (point.X >= min.X && point.X <= max.X &&
                point.Y >= min.Y && point.Y <= max.Y &&
                point.Z >= min.Z && point.Z <= max.Z)
            {
                result.Add(point);
            }
        }
        result.Sort(ComparePoints);

        errorMessage = string.Empty;
        return result;
    }
}

public sealed class AssignVoxelsToRegionServerTool : VoxelForgeMcpServerTool
{
    public AssignVoxelsToRegionServerTool(AssignVoxelsToRegionMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetRegionVoxelsMcpTool : RegionMcpToolBase
{
    public GetRegionVoxelsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "get_region_voxels",
            "Get coordinates and palette indices for all voxels assigned to a semantic region.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_id": { "type": "string" }
                },
                "required": ["region_id"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_id", out var regionIdText, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionId = new RegionId(regionIdText);
            if (!Session.Document.Labels.Regions.TryGetValue(regionId, out var region))
                return Fail($"Region '{regionIdText}' does not exist.");

            var points = new List<Point3>(region.Voxels);
            points.Sort(ComparePoints);
            var voxels = new List<Dictionary<string, object?>>();
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var paletteIndex = Session.Document.Model.GetVoxel(point);
                voxels.Add(new Dictionary<string, object?>
                {
                    ["x"] = point.X,
                    ["y"] = point.Y,
                    ["z"] = point.Z,
                    ["i"] = paletteIndex,
                    ["materialName"] = paletteIndex.HasValue ? Session.Document.Model.Palette.Get(paletteIndex.Value)?.Name ?? "unknown" : null,
                });
            }

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["regionId"] = regionId.Value,
                ["name"] = region.Name,
                ["voxels"] = voxels,
                ["count"] = voxels.Count,
            }));
        }
    }
}

public sealed class GetRegionVoxelsServerTool : VoxelForgeMcpServerTool
{
    public GetRegionVoxelsServerTool(GetRegionVoxelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetRegionBoundsMcpTool : RegionMcpToolBase
{
    public GetRegionBoundsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "get_region_bounds",
            "Get the axis-aligned bounding box for a semantic region.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "region_id": { "type": "string" }
                },
                "required": ["region_id"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredString(arguments, "region_id", out var regionIdText, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regionId = new RegionId(regionIdText);
            if (!Session.Document.Labels.Regions.TryGetValue(regionId, out var region))
                return Fail($"Region '{regionIdText}' does not exist.");

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["regionId"] = regionId.Value,
                ["name"] = region.Name,
                ["voxelCount"] = region.Voxels.Count,
                ["bounds"] = BuildBounds(region),
            }));
        }
    }
}

public sealed class GetRegionBoundsServerTool : VoxelForgeMcpServerTool
{
    public GetRegionBoundsServerTool(GetRegionBoundsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetRegionTreeMcpTool : RegionMcpToolBase
{
    public GetRegionTreeMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "get_region_tree",
            "Return the full semantic region hierarchy as a tree with descendants.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roots = new List<Dictionary<string, object?>>();
            var included = new HashSet<RegionId>();
            var rootIds = new List<RegionId>();

            foreach (var entry in Session.Document.Labels.Regions)
            {
                if (!entry.Value.ParentId.HasValue || !Session.Document.Labels.Regions.ContainsKey(entry.Value.ParentId.Value))
                    rootIds.Add(entry.Key);
            }
            rootIds.Sort(CompareRegionIds);

            for (int i = 0; i < rootIds.Count; i++)
                roots.Add(BuildTreeNode(Session.Document.Labels, rootIds[i], included));

            var remainingIds = new List<RegionId>();
            foreach (var entry in Session.Document.Labels.Regions)
            {
                if (!included.Contains(entry.Key))
                    remainingIds.Add(entry.Key);
            }
            remainingIds.Sort(CompareRegionIds);
            for (int i = 0; i < remainingIds.Count; i++)
                roots.Add(BuildTreeNode(Session.Document.Labels, remainingIds[i], included));

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["roots"] = roots,
                ["regionCount"] = Session.Document.Labels.Regions.Count,
            }));
        }
    }

    private static Dictionary<string, object?> BuildTreeNode(LabelIndex labels, RegionId regionId, HashSet<RegionId> included)
    {
        included.Add(regionId);
        var region = labels.Regions[regionId];
        var childIds = new List<RegionId>();
        foreach (var entry in labels.Regions)
        {
            if (entry.Value.ParentId == regionId)
                childIds.Add(entry.Key);
        }
        childIds.Sort(CompareRegionIds);

        var children = new List<Dictionary<string, object?>>();
        for (int i = 0; i < childIds.Count; i++)
        {
            if (!included.Contains(childIds[i]))
                children.Add(BuildTreeNode(labels, childIds[i], included));
        }

        return new Dictionary<string, object?>
        {
            ["id"] = regionId.Value,
            ["name"] = region.Name,
            ["voxelCount"] = region.Voxels.Count,
            ["bounds"] = BuildBounds(region),
            ["properties"] = new Dictionary<string, string>(region.Properties, StringComparer.Ordinal),
            ["children"] = children,
        };
    }

    private static int CompareRegionIds(RegionId left, RegionId right)
    {
        return string.CompareOrdinal(left.Value, right.Value);
    }
}

public sealed class GetRegionTreeServerTool : VoxelForgeMcpServerTool
{
    public GetRegionTreeServerTool(GetRegionTreeMcpTool tool)
        : base(tool)
    {
    }
}
