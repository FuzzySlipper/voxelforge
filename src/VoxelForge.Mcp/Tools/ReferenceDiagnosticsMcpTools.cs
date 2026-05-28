using System.Text.Json;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tools;

public sealed class GetReferenceModelDiagnosticsMcpTool : ModelLifecycleMcpToolBase
{
    public GetReferenceModelDiagnosticsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "get_reference_model_diagnostics",
            "Inspect a loaded reference model's bounds, dimensions, transform, materials, and warnings before voxelization. " +
            "Returns raw local AABB, world-space AABB (after transform), dimensions, center, max dimension, transform state, " +
            "mesh/vertex/triangle/material/texture summary, and warnings (tiny extent, flat axis, missing textures, no color variation, high triangle count).",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." }
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

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var rawAabb = ReferenceDiagnosticsHelper.ComputeRawAabb(model);
            var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
            var warnings = ReferenceDiagnosticsHelper.ComputeWarnings(model, worldAabb);
            var materialSummary = ReferenceDiagnosticsHelper.ComputeMaterialSummary(model);

            // Add Unity .mat sidecar diagnostics
            if (model.UnitySidecarResult is not null)
            {
                ReferenceDiagnosticsHelper.AddUnityMatSidecarDiagnostics(warnings, model.UnitySidecarResult, meshes: model.Meshes);
            }

            var rawSize = rawAabb.Size;
            var worldSize = worldAabb.Size;

            var diagnostics = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["file_name"] = Path.GetFileName(model.FilePath),
                ["format"] = model.Format,
                ["raw_bounds"] = new Dictionary<string, object?>
                {
                    ["min"] = new { x = rawAabb.Min.X, y = rawAabb.Min.Y, z = rawAabb.Min.Z },
                    ["max"] = new { x = rawAabb.Max.X, y = rawAabb.Max.Y, z = rawAabb.Max.Z },
                    ["size"] = new { x = rawSize.X, y = rawSize.Y, z = rawSize.Z },
                    ["center"] = new { x = rawAabb.Center.X, y = rawAabb.Center.Y, z = rawAabb.Center.Z },
                    ["max_dimension"] = rawAabb.MaxDimension,
                },
                ["world_bounds"] = new Dictionary<string, object?>
                {
                    ["min"] = new { x = worldAabb.Min.X, y = worldAabb.Min.Y, z = worldAabb.Min.Z },
                    ["max"] = new { x = worldAabb.Max.X, y = worldAabb.Max.Y, z = worldAabb.Max.Z },
                    ["size"] = new { x = worldSize.X, y = worldSize.Y, z = worldSize.Z },
                    ["center"] = new { x = worldAabb.Center.X, y = worldAabb.Center.Y, z = worldAabb.Center.Z },
                    ["max_dimension"] = worldAabb.MaxDimension,
                },
                ["transform"] = new Dictionary<string, object?>
                {
                    ["position"] = new { x = model.PositionX, y = model.PositionY, z = model.PositionZ },
                    ["rotation_degrees"] = new { x = model.RotationX, y = model.RotationY, z = model.RotationZ },
                    ["scale"] = model.Scale,
                },
                ["summary"] = new Dictionary<string, object?>
                {
                    ["meshes"] = model.Meshes.Count,
                    ["total_vertices"] = model.TotalVertices,
                    ["total_triangles"] = model.TotalTriangles,
                    ["materials"] = materialSummary.MaterialCount,
                    ["material_names"] = materialSummary.MaterialNames,
                    ["meshes_with_diffuse_texture"] = materialSummary.MeshesWithDiffuseTexture,
                    ["meshes_with_emissive_texture"] = materialSummary.MeshesWithEmissiveTexture,
                    ["has_animations"] = model.HasAnimations,
                    ["is_visible"] = model.IsVisible,
                    ["render_mode"] = model.RenderMode.ToString().ToLowerInvariant(),
                },
                ["warnings"] = warnings.Select(w => new Dictionary<string, object?>
                {
                    ["code"] = w.Code,
                    ["message"] = w.Message,
                    ["severity"] = w.Severity,
                }).ToList(),
                ["unity_sidecar"] = model.UnitySidecarResult is not null
                    ? BuildUnitySidecarDiagnostics(model.UnitySidecarResult)
                    : null,
            };

            return Ok(SerializeJson(diagnostics));
        }
    }

    private static Dictionary<string, object?>? BuildUnitySidecarDiagnostics(UnityMatSidecarResult sidecar)
    {
        if (!sidecar.FoundAnyMatFiles)
            return new Dictionary<string, object?>
            {
                ["found"] = false,
                ["matches"] = new List<object>(),
            };

        var matches = new List<Dictionary<string, object?>>();
        foreach (var m in sidecar.Matches)
        {
            var matchData = new Dictionary<string, object?>
            {
                ["mat_file"] = Path.GetFileName(m.MatFilePath),
                ["material_name"] = m.MatchedMaterialName,
                ["match_kind"] = m.MatchKind.ToString(),
                ["matched"] = m.ParsedData is not null,
            };

            if (m.ParsedData is not null)
            {
                matchData["resolved_textures"] = m.ParsedData.ResolvedTextures.Count > 0
                    ? m.ParsedData.ResolvedTextures
                    : null;
                matchData["has_tint"] = m.ParsedData.MainColor.HasValue;
                matchData["has_emission"] = m.ParsedData.EmissionColor.HasValue || m.ParsedData.EmissionMap is not null;
                matchData["unresolved_guids"] = m.ParsedData.UnresolvedGuids.Count > 0
                    ? m.ParsedData.UnresolvedGuids : null;
                matchData["ignored_properties"] = m.ParsedData.IgnoredProperties.Count > 0
                    ? m.ParsedData.IgnoredProperties.Take(10).ToList() : null;
            }

            if (m.Warnings.Count > 0)
                matchData["warnings"] = m.Warnings;

            matches.Add(matchData);
        }

        return new Dictionary<string, object?>
        {
            ["found"] = true,
            ["matches"] = matches,
            ["global_warnings"] = sidecar.GlobalWarnings.Count > 0 ? sidecar.GlobalWarnings : null,
        };
    }
}

public sealed class GetReferenceModelDiagnosticsServerTool : VoxelForgeMcpServerTool
{
    public GetReferenceModelDiagnosticsServerTool(GetReferenceModelDiagnosticsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class SuggestReferenceTransformMcpTool : ModelLifecycleMcpToolBase
{
    public SuggestReferenceTransformMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "suggest_reference_transform",
            "Dry-run scale and translation suggestion for a loaded reference model. " +
            "Returns recommended scale to match a target height or max dimension, and optional centering translation. Does not mutate state.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "target_height": { "type": "number", "description": "Target height along the chosen axis." },
                    "target_max_dim": { "type": "number", "description": "Target max dimension (ignores axis). Mutually exclusive with target_height." },
                    "axis": { "type": "string", "enum": ["x", "y", "z", "max"], "description": "Axis for target_height. Defaults to 'y'. Ignored if target_max_dim is used." },
                    "center": { "type": "boolean", "description": "Whether to suggest centering on X/Z. Defaults to true." }
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

        bool hasTargetHeight = false;
        float targetHeight = 0;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("target_height", out var thEl) && thEl.ValueKind == JsonValueKind.Number)
        {
            if (!thEl.TryGetSingle(out targetHeight))
                return Fail("Property 'target_height' must be a number.");
            hasTargetHeight = true;
        }

        bool hasTargetMaxDim = false;
        float targetMaxDim = 0;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("target_max_dim", out var mdEl) && mdEl.ValueKind == JsonValueKind.Number)
        {
            if (!mdEl.TryGetSingle(out targetMaxDim))
                return Fail("Property 'target_max_dim' must be a number.");
            hasTargetMaxDim = true;
        }

        if (hasTargetHeight && hasTargetMaxDim)
            return Fail("Specify either 'target_height' or 'target_max_dim', not both.");

        if (!hasTargetHeight && !hasTargetMaxDim)
            return Fail("Either 'target_height' or 'target_max_dim' is required.");

        var axisStr = "y";
        bool center = true;
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("axis", out var axisEl) && axisEl.ValueKind == JsonValueKind.String)
                axisStr = axisEl.GetString() ?? "y";
            if (arguments.TryGetProperty("center", out var centerEl) && centerEl.ValueKind == JsonValueKind.False)
                center = false;
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var unitAabb = ReferenceDiagnosticsHelper.ComputeUnitScaleWorldAabb(model);
            var unitSize = unitAabb.Max - unitAabb.Min;

            HeightAxis heightAxis;
            float targetValue;

            if (hasTargetMaxDim)
            {
                heightAxis = HeightAxis.MaxDim;
                targetValue = targetMaxDim;
            }
            else
            {
                heightAxis = axisStr.ToLowerInvariant() switch
                {
                    "x" => HeightAxis.X,
                    "y" => HeightAxis.Y,
                    "z" => HeightAxis.Z,
                    "max" => HeightAxis.MaxDim,
                    _ => HeightAxis.Y,
                };
                targetValue = targetHeight;
            }

            float suggestedScale = ReferenceDiagnosticsHelper.SuggestScaleForTargetHeight(model, targetValue, heightAxis);

            // Build suggestion result
            var unitDim = heightAxis switch
            {
                HeightAxis.MaxDim => unitAabb.MaxDimension,
                HeightAxis.X => unitSize.X,
                HeightAxis.Y => unitSize.Y,
                HeightAxis.Z => unitSize.Z,
                _ => unitAabb.MaxDimension,
            };

            var result = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["current_scale"] = model.Scale,
                ["suggested_scale"] = suggestedScale,
                ["target_value"] = targetValue,
                ["axis"] = axisStr.ToLowerInvariant(),
                ["current_unit_world_dimension"] = unitDim,
                ["current_world_dimension"] = unitDim * model.Scale,
                ["expected_world_dimension_after_scale"] = unitDim * suggestedScale,
            };

            if (center)
            {
                // Suggest centering on X/Z, feet at Y=0
                var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
                // Apply the suggested scale to compute suggested translation
                // We need a temp model state or just compute from raw AABB + suggested scale + current rotation
                var tempModel = new ReferenceModelData
                {
                    FilePath = model.FilePath,
                    Format = model.Format,
                    Meshes = model.Meshes,
                    PositionX = 0,
                    PositionY = 0,
                    PositionZ = 0,
                    RotationX = model.RotationX,
                    RotationY = model.RotationY,
                    RotationZ = model.RotationZ,
                    Scale = suggestedScale,
                };
                var suggestedWorldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(tempModel);
                var suggestedWorldSize = suggestedWorldAabb.Max - suggestedWorldAabb.Min;

                result["suggested_position"] = new Dictionary<string, object?>
                {
                    ["x"] = -(suggestedWorldAabb.Min.X + suggestedWorldAabb.Max.X) / 2f,
                    ["y"] = -suggestedWorldAabb.Min.Y,
                    ["z"] = -(suggestedWorldAabb.Min.Z + suggestedWorldAabb.Max.Z) / 2f,
                };
                result["center"] = true;
            }
            else
            {
                result["center"] = false;
                result["suggested_position"] = new Dictionary<string, object?>
                {
                    ["x"] = model.PositionX,
                    ["y"] = model.PositionY,
                    ["z"] = model.PositionZ,
                };
            }

            return Ok(SerializeJson(result));
        }
    }
}

public sealed class SuggestReferenceTransformServerTool : VoxelForgeMcpServerTool
{
    public SuggestReferenceTransformServerTool(SuggestReferenceTransformMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class FitReferenceModelMcpTool : ModelLifecycleMcpToolBase
{
    public FitReferenceModelMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "fit_reference_model",
            "Apply a recommended scale and optional centering translation to a loaded reference model. " +
            "Returns before/after diagnostics. Mutates the model's transform (scale, position) — consistent with existing transform_reference_model (no undo for reference transforms).",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "target_height": { "type": "number", "description": "Target height along the chosen axis." },
                    "target_max_dim": { "type": "number", "description": "Target max dimension (ignores axis). Mutually exclusive with target_height." },
                    "axis": { "type": "string", "enum": ["x", "y", "z", "max"], "description": "Axis for target_height. Defaults to 'y'. Ignored if target_max_dim is used." },
                    "center": { "type": "boolean", "description": "Whether to center on X/Z and set feet at Y=0. Defaults to true." }
                },
                "required": ["index"]
            }
            """),
            isReadOnly: false)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        bool hasTargetHeight = false;
        float targetHeight = 0;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("target_height", out var thEl) && thEl.ValueKind == JsonValueKind.Number)
        {
            if (!thEl.TryGetSingle(out targetHeight))
                return Fail("Property 'target_height' must be a number.");
            hasTargetHeight = true;
        }

        bool hasTargetMaxDim = false;
        float targetMaxDim = 0;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("target_max_dim", out var mdEl) && mdEl.ValueKind == JsonValueKind.Number)
        {
            if (!mdEl.TryGetSingle(out targetMaxDim))
                return Fail("Property 'target_max_dim' must be a number.");
            hasTargetMaxDim = true;
        }

        if (hasTargetHeight && hasTargetMaxDim)
            return Fail("Specify either 'target_height' or 'target_max_dim', not both.");

        if (!hasTargetHeight && !hasTargetMaxDim)
            return Fail("Either 'target_height' or 'target_max_dim' is required.");

        var axisStr = "y";
        bool center = true;
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("axis", out var axisEl) && axisEl.ValueKind == JsonValueKind.String)
                axisStr = axisEl.GetString() ?? "y";
            if (arguments.TryGetProperty("center", out var centerEl) && centerEl.ValueKind == JsonValueKind.False)
                center = false;
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            // Capture before diagnostics (before mutation)
            var beforeScale = model.Scale;
            var beforePosX = model.PositionX;
            var beforePosY = model.PositionY;
            var beforePosZ = model.PositionZ;
            var beforeRotX = model.RotationX;
            var beforeRotY = model.RotationY;
            var beforeRotZ = model.RotationZ;
            var beforeRawAabb = ReferenceDiagnosticsHelper.ComputeRawAabb(model);
            var beforeWorldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);

            HeightAxis heightAxis;
            float targetValue;

            if (hasTargetMaxDim)
            {
                heightAxis = HeightAxis.MaxDim;
                targetValue = targetMaxDim;
            }
            else
            {
                heightAxis = axisStr.ToLowerInvariant() switch
                {
                    "x" => HeightAxis.X,
                    "y" => HeightAxis.Y,
                    "z" => HeightAxis.Z,
                    "max" => HeightAxis.MaxDim,
                    _ => HeightAxis.Y,
                };
                targetValue = targetHeight;
            }

            float suggestedScale = ReferenceDiagnosticsHelper.SuggestScaleForTargetHeight(model, targetValue, heightAxis);

            // Apply scale
            model.Scale = suggestedScale;

            if (center)
            {
                // Build a temp model with the new scale to compute centering translation
                var tempModel = new ReferenceModelData
                {
                    FilePath = model.FilePath,
                    Format = model.Format,
                    Meshes = model.Meshes,
                    PositionX = 0,
                    PositionY = 0,
                    PositionZ = 0,
                    RotationX = model.RotationX,
                    RotationY = model.RotationY,
                    RotationZ = model.RotationZ,
                    Scale = suggestedScale,
                };
                var afterWorldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(tempModel);

                model.PositionX = -(afterWorldAabb.Min.X + afterWorldAabb.Max.X) / 2f;
                model.PositionY = -afterWorldAabb.Min.Y;
                model.PositionZ = -(afterWorldAabb.Min.Z + afterWorldAabb.Max.Z) / 2f;
            }

            // Capture after diagnostics
            var afterRawAabb = ReferenceDiagnosticsHelper.ComputeRawAabb(model);
            var afterWorldAabb2 = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);

            var rawBeforeSize = beforeRawAabb.Size;
            var worldBeforeSize = beforeWorldAabb.Size;
            var rawAfterSize = afterRawAabb.Size;
            var worldAfterSize = afterWorldAabb2.Size;

            Session.Events.Publish(new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.TransformChanged,
                $"Fitted reference model [{index}] scale={suggestedScale} center={center}",
                index));

            var result = new Dictionary<string, object?>
            {
                ["index"] = index,
                ["before"] = new Dictionary<string, object?>
                {
                    ["transform"] = new Dictionary<string, object?>
                    {
                        ["position"] = new { x = beforePosX, y = beforePosY, z = beforePosZ },
                        ["rotation_degrees"] = new { x = beforeRotX, y = beforeRotY, z = beforeRotZ },
                        ["scale"] = beforeScale,
                    },
                    ["world_bounds"] = new Dictionary<string, object?>
                    {
                        ["min"] = new { x = beforeWorldAabb.Min.X, y = beforeWorldAabb.Min.Y, z = beforeWorldAabb.Min.Z },
                        ["max"] = new { x = beforeWorldAabb.Max.X, y = beforeWorldAabb.Max.Y, z = beforeWorldAabb.Max.Z },
                        ["size"] = new { x = worldBeforeSize.X, y = worldBeforeSize.Y, z = worldBeforeSize.Z },
                        ["max_dimension"] = beforeWorldAabb.MaxDimension,
                    },
                },
                ["after"] = new Dictionary<string, object?>
                {
                    ["transform"] = new Dictionary<string, object?>
                    {
                        ["position"] = new { x = model.PositionX, y = model.PositionY, z = model.PositionZ },
                        ["rotation_degrees"] = new { x = model.RotationX, y = model.RotationY, z = model.RotationZ },
                        ["scale"] = model.Scale,
                    },
                    ["world_bounds"] = new Dictionary<string, object?>
                    {
                        ["min"] = new { x = afterWorldAabb2.Min.X, y = afterWorldAabb2.Min.Y, z = afterWorldAabb2.Min.Z },
                        ["max"] = new { x = afterWorldAabb2.Max.X, y = afterWorldAabb2.Max.Y, z = afterWorldAabb2.Max.Z },
                        ["size"] = new { x = worldAfterSize.X, y = worldAfterSize.Y, z = worldAfterSize.Z },
                        ["max_dimension"] = afterWorldAabb2.MaxDimension,
                    },
                },
            };

            return Ok(SerializeJson(result));
        }
    }
}

public sealed class FitReferenceModelServerTool : VoxelForgeMcpServerTool
{
    public FitReferenceModelServerTool(FitReferenceModelMcpTool tool)
        : base(tool)
    {
    }
}
