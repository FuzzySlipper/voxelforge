using System.Text.Json;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP tool: raycast_reference_model — cast a ray against loaded reference mesh triangles.
/// Returns hit count, nearest hits with distance/point/normal, mesh index, triangle index.
/// </summary>
public sealed class RaycastReferenceModelMcpTool : ModelLifecycleMcpToolBase
{
    public RaycastReferenceModelMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "raycast_reference_model",
            "Cast a ray against a loaded reference model's mesh triangles (transformed or local space) " +
            "and return hit count, nearest hits with distance/point/normal, mesh index, and triangle index. " +
            "Validates direction is non-zero and model index is valid. Cap max_hits at 1000.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "origin": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "description": "Ray origin in world space."
                    },
                    "direction": {
                        "type": "object",
                        "properties": {
                            "x": { "type": "number" },
                            "y": { "type": "number" },
                            "z": { "type": "number" }
                        },
                        "required": ["x", "y", "z"],
                        "description": "Ray direction vector. Must be non-zero."
                    },
                    "max_hits": { "type": "integer", "default": 10, "minimum": 1, "maximum": 1000, "description": "Maximum number of nearest hits to return (1-1000)." },
                    "transformed": { "type": "boolean", "default": true, "description": "Use transformed (world-space) coordinates matching rendering/voxelization." }
                },
                "required": ["index", "origin", "direction"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        if (!TryReadVec3(arguments, "origin", out var origin, out errorMessage))
            return Fail(errorMessage);

        if (!TryReadVec3(arguments, "direction", out var direction, out errorMessage))
            return Fail(errorMessage);

        if (direction.Length() < float.Epsilon)
            return Fail("Direction vector must be non-zero (length > 0).");

        int maxHits = 10;
        if (arguments.TryGetProperty("max_hits", out var maxHitsEl) && maxHitsEl.ValueKind == JsonValueKind.Number)
            maxHits = Math.Clamp(maxHitsEl.GetInt32(), 1, ReferenceMeshProbeHelper.MaxRaycastHits);

        bool transformed = true;
        if (arguments.TryGetProperty("transformed", out var transformedEl) && transformedEl.ValueKind == JsonValueKind.False)
            transformed = false;

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var result = ReferenceMeshProbeHelper.RaycastReferenceModel(
                model, origin, direction, maxHits, transformed);

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["hit_count"] = result.HitCount,
                ["total_meshes"] = result.TotalMeshes,
                ["total_triangles"] = result.TotalTriangles,
                ["hits"] = result.Hits?.Select(h => new Dictionary<string, object?>
                {
                    ["mesh_index"] = h.MeshIndex,
                    ["triangle_index"] = h.TriangleIndex,
                    ["distance"] = h.Distance,
                    ["point"] = new { x = h.Point.X, y = h.Point.Y, z = h.Point.Z },
                    ["normal"] = new { x = h.Normal.X, y = h.Normal.Y, z = h.Normal.Z },
                    ["material_name"] = h.MaterialName,
                    ["color"] = new { r = h.R, g = h.G, b = h.B },
                }).ToList(),
            }));
        }
    }

    private static bool TryReadVec3(JsonElement arguments, string propertyName, out System.Numerics.Vector3 value, out string errorMessage)
    {
        value = default;
        errorMessage = string.Empty;

        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(propertyName, out var obj))
        {
            errorMessage = $"Missing required property '{propertyName}'.";
            return false;
        }

        if (obj.ValueKind != JsonValueKind.Object)
        {
            errorMessage = $"Property '{propertyName}' must be an object {{x, y, z}}.";
            return false;
        }

        if (!obj.TryGetProperty("x", out var xEl) || xEl.ValueKind != JsonValueKind.Number || !xEl.TryGetSingle(out float x) ||
            !obj.TryGetProperty("y", out var yEl) || yEl.ValueKind != JsonValueKind.Number || !yEl.TryGetSingle(out float y) ||
            !obj.TryGetProperty("z", out var zEl) || zEl.ValueKind != JsonValueKind.Number || !zEl.TryGetSingle(out float z))
        {
            errorMessage = $"Property '{propertyName}' must contain numeric fields 'x', 'y', 'z'.";
            return false;
        }

        value = new System.Numerics.Vector3(x, y, z);
        return true;
    }
}

public sealed class RaycastReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public RaycastReferenceModelServerTool(RaycastReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// MCP tool: sample_reference_model_views — orthographic silhouette/depth probes
/// from canonical view directions.
/// </summary>
public sealed class SampleReferenceModelViewsMcpTool : ModelLifecycleMcpToolBase
{
    public SampleReferenceModelViewsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "sample_reference_model_views",
            "Sample orthographic silhouette/depth probes of a loaded reference model from canonical " +
            "views (front, right, top, back, left, bottom). Returns compact run-length-encoded occupancy " +
            "grids and depth ranges. Cap resolution and view count for bounded output.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "views": {
                        "type": "array",
                        "items": { "type": "string", "enum": ["front", "right", "top", "back", "left", "bottom"] },
                        "description": "View directions to sample. Default: front, right, top."
                    },
                    "resolution": { "type": "integer", "default": 64, "minimum": 1, "maximum": 128, "description": "Orthographic probe resolution per axis (1-128)." }
                },
                "required": ["index"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        int resolution = 64;
        if (arguments.TryGetProperty("resolution", out var resEl) && resEl.ValueKind == JsonValueKind.Number)
            resolution = Math.Clamp(resEl.GetInt32(), 1, ReferenceMeshProbeHelper.MaxOrthoResolution);

        string[]? views = null;
        if (arguments.TryGetProperty("views", out var viewsEl) && viewsEl.ValueKind == JsonValueKind.Array)
        {
            views = viewsEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Take(ReferenceMeshProbeHelper.MaxViewCount)
                .ToArray();
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var result = ReferenceMeshProbeHelper.SampleReferenceModelViews(model, views, resolution);

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["view_count"] = result.ViewCount,
                ["total_occupied_samples"] = result.TotalOccupiedSamples,
                ["views"] = result.Views.Select(v => new Dictionary<string, object?>
                {
                    ["view_name"] = v.ViewName,
                    ["view_axis"] = new { x = v.ViewAxis.X, y = v.ViewAxis.Y, z = v.ViewAxis.Z },
                    ["up_axis"] = new { x = v.UpAxis.X, y = v.UpAxis.Y, z = v.UpAxis.Z },
                    ["projection_bounds"] = new
                    {
                        min = new { x = v.ProjectionMin.X, y = v.ProjectionMin.Y },
                        max = new { x = v.ProjectionMax.X, y = v.ProjectionMax.Y },
                    },
                    ["resolution"] = v.Resolution,
                    ["occupied_samples"] = v.OccupiedSamples,
                    ["occupancy_density"] = v.OccupancyDensity,
                    ["depth_min"] = v.DepthMin,
                    ["depth_max"] = v.DepthMax,
                    ["median_depth"] = v.MedianDepth,
                    ["run_length_rows"] = v.RunLengthRows,
                }).ToList(),
            }));
        }
    }
}

public sealed class SampleReferenceModelViewsServerTool : VoxelForgeMcpServerTool
{
    public SampleReferenceModelViewsServerTool(SampleReferenceModelViewsMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// MCP tool: reference_model_axis_histogram — vertex position distribution along
/// one axis for scale/orientation sanity checks.
/// </summary>
public sealed class ReferenceModelAxisHistogramMcpTool : ModelLifecycleMcpToolBase
{
    public ReferenceModelAxisHistogramMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "reference_model_axis_histogram",
            "Compute approximate distribution of vertex positions along a single axis " +
            "(world-space by default). Useful for scale/orientation sanity checks, " +
            "detecting flat axes, and understanding shape distribution. Cap bins at 256.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "axis": { "type": "string", "enum": ["x", "y", "z"], "default": "y", "description": "Axis to project vertices onto." },
                    "bins": { "type": "integer", "default": 32, "minimum": 1, "maximum": 256, "description": "Number of histogram bins (1-256)." },
                    "transformed": { "type": "boolean", "default": true, "description": "Use transformed (world-space) vertex positions." }
                },
                "required": ["index"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        string axisStr = "y";
        if (arguments.TryGetProperty("axis", out var axisEl) && axisEl.ValueKind == JsonValueKind.String)
        {
            var s = axisEl.GetString()?.ToLowerInvariant();
            if (s is "x" or "y" or "z")
                axisStr = s;
            else
                return Fail("Axis must be 'x', 'y', or 'z'.");
        }

        int bins = 32;
        if (arguments.TryGetProperty("bins", out var binsEl) && binsEl.ValueKind == JsonValueKind.Number)
            bins = Math.Clamp(binsEl.GetInt32(), 1, ReferenceMeshProbeHelper.MaxHistogramBins);

        bool transformed = true;
        if (arguments.TryGetProperty("transformed", out var transformedEl) && transformedEl.ValueKind == JsonValueKind.False)
            transformed = false;

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var result = ReferenceMeshProbeHelper.ComputeAxisHistogram(model, axisStr[0], bins, transformed);

            return Ok(SerializeJson(new Dictionary<string, object?>
            {
                ["axis"] = result.Axis,
                ["bin_count"] = result.BinCount,
                ["bin_width"] = result.BinWidth,
                ["min_value"] = result.MinValue,
                ["max_value"] = result.MaxValue,
                ["mean"] = result.Mean,
                ["median"] = result.Median,
                ["total_samples"] = result.TotalSamples,
                ["counts"] = result.Counts.ToList(),
            }));
        }
    }
}

public sealed class ReferenceModelAxisHistogramServerTool : VoxelForgeMcpServerTool
{
    public ReferenceModelAxisHistogramServerTool(ReferenceModelAxisHistogramMcpTool tool)
        : base(tool)
    {
    }
}
