using System.Numerics;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Reference;

/// <summary>
/// Shared math for reference model diagnostics, transform suggestions, and warnings.
/// Uses the same transform order as VoxelizeCommand and renderer: Scale → YawPitchRoll → Translation.
/// </summary>
public static class ReferenceDiagnosticsHelper
{
    /// <summary>
    /// Raw (local, untransformed) axis-aligned bounding box of all vertices.
    /// </summary>
    public static AabbResult ComputeRawAabb(ReferenceModelData model)
    {
        bool first = true;
        float minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;

        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                if (first)
                {
                    minX = maxX = v.PosX;
                    minY = maxY = v.PosY;
                    minZ = maxZ = v.PosZ;
                    first = false;
                }
                else
                {
                    if (v.PosX < minX) minX = v.PosX;
                    if (v.PosY < minY) minY = v.PosY;
                    if (v.PosZ < minZ) minZ = v.PosZ;
                    if (v.PosX > maxX) maxX = v.PosX;
                    if (v.PosY > maxY) maxY = v.PosY;
                    if (v.PosZ > maxZ) maxZ = v.PosZ;
                }
            }
        }

        return new AabbResult
        {
            Min = new Vector3(minX, minY, minZ),
            Max = new Vector3(maxX, maxY, maxZ),
        };
    }

    /// <summary>
    /// Compute the world-space AABB by applying the model's full transform
    /// (scale → yaw/pitch/roll → translation) to all vertices.
    /// </summary>
    public static AabbResult ComputeTransformedAabb(ReferenceModelData model)
    {
        var transform = BuildTransform(model);
        bool first = true;
        float minX = 0, minY = 0, minZ = 0, maxX = 0, maxY = 0, maxZ = 0;

        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                var pos = Vector3.Transform(new Vector3(v.PosX, v.PosY, v.PosZ), transform);

                if (first)
                {
                    minX = maxX = pos.X;
                    minY = maxY = pos.Y;
                    minZ = maxZ = pos.Z;
                    first = false;
                }
                else
                {
                    if (pos.X < minX) minX = pos.X;
                    if (pos.Y < minY) minY = pos.Y;
                    if (pos.Z < minZ) minZ = pos.Z;
                    if (pos.X > maxX) maxX = pos.X;
                    if (pos.Y > maxY) maxY = pos.Y;
                    if (pos.Z > maxZ) maxZ = pos.Z;
                }
            }
        }

        return new AabbResult
        {
            Min = new Vector3(minX, minY, minZ),
            Max = new Vector3(maxX, maxY, maxZ),
        };
    }

    /// <summary>
    /// Build the Scale → YawPitchRoll → Translation matrix matching VoxelizeCommand.
    /// Yaw = RotationY, Pitch = RotationX, Roll = RotationZ.
    /// </summary>
    public static Matrix4x4 BuildTransform(ReferenceModelData model)
    {
        return Matrix4x4.CreateScale(model.Scale)
            * Matrix4x4.CreateFromYawPitchRoll(
                float.DegreesToRadians(model.RotationY),
                float.DegreesToRadians(model.RotationX),
                float.DegreesToRadians(model.RotationZ))
            * Matrix4x4.CreateTranslation(model.PositionX, model.PositionY, model.PositionZ);
    }

    /// <summary>
    /// Compute a suggested absolute scale to make the model's transformed max dimension
    /// (or a specific transformed world axis) match a target value. The dimension basis
    /// uses the same scale → yaw/pitch/roll → translation order as voxelization, with
    /// scale normalized to 1 and translation set to zero so rotation-induced AABB changes
    /// are included without current scale/position affecting the recommendation.
    /// Returns the model's current scale if target is zero or negative.
    /// </summary>
    public static float SuggestScaleForTargetHeight(
        ReferenceModelData model,
        float targetValue,
        HeightAxis axis)
    {
        if (targetValue <= 0)
            return model.Scale;

        var unitAabb = ComputeUnitScaleWorldAabb(model);
        var unitSize = unitAabb.Max - unitAabb.Min;

        float currentDimension = axis switch
        {
            HeightAxis.MaxDim => MathF.Max(unitSize.X, MathF.Max(unitSize.Y, unitSize.Z)),
            HeightAxis.X => unitSize.X,
            HeightAxis.Y => unitSize.Y,
            HeightAxis.Z => unitSize.Z,
            _ => MathF.Max(unitSize.X, MathF.Max(unitSize.Y, unitSize.Z)),
        };

        if (currentDimension <= float.Epsilon)
            return model.Scale;

        return targetValue / currentDimension;
    }

    /// <summary>
    /// Compute the transformed AABB using current rotations, unit scale, and zero translation.
    /// This is the correct basis for absolute scale suggestions because world AABB dimensions
    /// can change under rotation even before scale is applied.
    /// </summary>
    public static AabbResult ComputeUnitScaleWorldAabb(ReferenceModelData model)
    {
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
            Scale = 1f,
        };

        return ComputeTransformedAabb(tempModel);
    }

    /// <summary>
    /// Generate diagnostic warnings for a loaded reference model.
    /// </summary>
    public static List<DiagnosticWarning> ComputeWarnings(ReferenceModelData model, AabbResult transformedAabb)
    {
        var warnings = new List<DiagnosticWarning>();
        var worldSize = transformedAabb.Max - transformedAabb.Min;
        float maxDim = MathF.Max(worldSize.X, MathF.Max(worldSize.Y, worldSize.Z));

        // Tiny extent warning
        if (maxDim < 0.01f)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "tiny_extent",
                Message = $"Transformed max dimension is {maxDim:G3} — model may be too small to voxelize. Try increasing scale.",
                Severity = "warning",
            });
        }
        else if (maxDim < 0.1f)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "small_extent",
                Message = $"Transformed max dimension is {maxDim:G3} — model may produce very few voxels. Consider increasing scale.",
                Severity = "info",
            });
        }

        // Flat axis warning
        if (worldSize.X < 0.001f || worldSize.Y < 0.001f || worldSize.Z < 0.001f)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "flat_axis",
                Message = $"One or more axes have near-zero extent in world space (size={worldSize}). Model may be flat or degenerate.",
                Severity = "warning",
            });
        }

        // Missing textures
        bool anyMissingTexture = false;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.DiffuseTexturePath is not null && !File.Exists(mesh.DiffuseTexturePath))
            {
                anyMissingTexture = true;
                break;
            }
        }
        if (anyMissingTexture)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "missing_texture",
                Message = "One or more meshes reference a diffuse texture file that does not exist on disk.",
                Severity = "info",
            });
        }

        // Meshes with textures but no UVs
        bool anyTextureWithoutUvs = false;
        bool anyMeshHasUvs = false;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.HasUvs)
                anyMeshHasUvs = true;
            if (mesh.EffectiveDiffuseTexturePath is not null && !mesh.HasUvs)
                anyTextureWithoutUvs = true;
        }
        if (anyTextureWithoutUvs)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "texture_without_uvs",
                Message = "One or more meshes have a diffuse texture but no UV coordinates. The viewer will fall back to vertex colors and the texture will not be visible. Apply UVs in the source asset or use set_reference_model_texture only on UV-bearing meshes.",
                Severity = "warning",
            });
        }

        // Texture present — viewer now supports texture loading via THREE.TextureLoader
        // (task #1648). Textures are served via /api/reference-texture and applied
        // to MeshStandardMaterial map. The texture load is async; the vertex color
        // fallback is used when texture fails to load or is unavailable.
        bool anyTexturePresent = false;
        foreach (var mesh in model.Meshes)
        {
            if (mesh.EffectiveDiffuseTexturePath is not null && File.Exists(mesh.EffectiveDiffuseTexturePath))
            {
                anyTexturePresent = true;
                break;
            }
        }
        if (anyTexturePresent)
        {
            var uvStatus = anyMeshHasUvs ? "UVs present" : "no UVs detected";
            warnings.Add(new DiagnosticWarning
            {
                Code = "texture_available",
                Message = "One or more meshes have a diffuse texture (" + uvStatus + "). The viewer will attempt to load and " +
                          "display it via the THREE.TextureLoader (async). Overrides labelled manual_override " +
                          "are session-only and will not persist across restarts.",
                Severity = "info",
            });
        }

        // No color variation — check all vertices in all meshes
        bool hasColorVariation = false;
        (byte r, byte g, byte b, byte a)? firstColor = null;
        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                var c = (v.R, v.G, v.B, v.A);
                if (firstColor is null)
                {
                    firstColor = c;
                }
                else if (c != firstColor.Value)
                {
                    hasColorVariation = true;
                    break;
                }
            }
            if (hasColorVariation) break;
        }
        if (!hasColorVariation && model.TotalVertices > 0)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "no_color_variation",
                Message = "All vertices share the same color. Voxelization/viewer output will be monochrome unless textures " +
                          "or material baking are available. If the source asset has image files but diagnostics show zero " +
                          "diffuse textures, the importer did not link/bake them into vertex colors; the viewer is not " +
                          "ignoring a known texture path.",
                Severity = "info",
            });
        }

        // High triangle count
        if (model.TotalTriangles > 100_000)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "high_triangle_count",
                Message = $"Model has {model.TotalTriangles:N0} triangles, which may slow voxelization. Consider decimation.",
                Severity = "info",
            });
        }

        return warnings;
    }

    /// <summary>
    /// Summarize mesh material and texture info.
    /// </summary>
    public static MaterialSummary ComputeMaterialSummary(ReferenceModelData model)
    {
        var materialNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int diffuseTextures = 0;
        int emissiveTextures = 0;

        foreach (var mesh in model.Meshes)
        {
            if (!string.IsNullOrWhiteSpace(mesh.MaterialName))
                materialNames.Add(mesh.MaterialName);
            if (mesh.DiffuseTexturePath is not null)
                diffuseTextures++;
            if (mesh.EmissiveTexturePath is not null)
                emissiveTextures++;
        }

        return new MaterialSummary
        {
            MaterialCount = materialNames.Count,
            MaterialNames = materialNames.ToList(),
            MeshesWithDiffuseTexture = diffuseTextures,
            MeshesWithEmissiveTexture = emissiveTextures,
        };
    }

    /// <summary>
    /// Add Unity .mat sidecar diagnostic warnings if sidecar processing produced them.
    /// Also reports mixed provenance when both Unity sidecar and .vf-reference-settings.json
    /// contributed to material/texture/sampling state.
    /// </summary>
    public static void AddUnityMatSidecarDiagnostics(
        List<DiagnosticWarning> warnings,
        UnityMatSidecarResult? sidecarResult,
        IReadOnlyList<string>? materialNames = null,
        IReadOnlyList<ReferenceMeshData>? meshes = null)
    {
        if (sidecarResult is null)
            return;

        if (!sidecarResult.FoundAnyMatFiles)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "no_unity_mat_sidecars",
                Message = "No Unity .mat sidecar files found. Old Unity assets may lack original texture references.",
                Severity = "info",
            });
            return;
        }

        int matched = sidecarResult.Matches.Count(m => m.ParsedData is not null);
        int totalMaterials = sidecarResult.Matches.Count;

        if (matched > 0)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "unity_mat_sidecars_matched",
                Message = $"Unity .mat sidecars: {matched}/{totalMaterials} materials matched, " +
                          $"{sidecarResult.Matches.Sum(m => m.ParsedData?.ResolvedTextures.Count ?? 0)} textures resolved.",
                Severity = "info",
            });

            foreach (var match in sidecarResult.Matches)
            {
                if (match.ParsedData is null)
                    continue;

                var mat = match.ParsedData;
                if (mat.UnresolvedGuids.Count > 0)
                {
                    warnings.Add(new DiagnosticWarning
                    {
                        Code = "unresolved_texture_guids",
                        Message = $"Material '{match.MatchedMaterialName}': {mat.UnresolvedGuids.Count} texture GUID(s) could not be resolved via .meta files.",
                        Severity = "warning",
                    });
                }

                if (mat.IgnoredProperties.Count > 0)
                {
                    warnings.Add(new DiagnosticWarning
                    {
                        Code = "ignored_unity_properties",
                        Message = $"Material '{match.MatchedMaterialName}': {mat.IgnoredProperties.Count} unsupported Unity shader properties ignored ({string.Join(", ", mat.IgnoredProperties.Take(5))}{(mat.IgnoredProperties.Count > 5 ? "..." : "")}).",
                        Severity = "info",
                    });
                }
            }
        }
        else
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "unmatched_unity_mat_sidecars",
                Message = $"Found {sidecarResult.Matches.Count} .mat file(s) but none matched any material by name or filename stem.",
                Severity = "info",
            });
        }

        // Surface global warnings
        foreach (var gw in sidecarResult.GlobalWarnings)
        {
            warnings.Add(new DiagnosticWarning
            {
                Code = "unity_mat_sidecar_note",
                Message = gw,
                Severity = "info",
            });
        }

        // Mixed provenance diagnostics: when meshes are provided, report cases
        // where Unity sidecar and .vf-reference-settings.json (or manual overrides)
        // each contributed different parts of the material state (texture source vs
        // sampling controls) for the same mesh.
        if (meshes is not null && meshes.Count > 0)
        {
            bool reportedMixedProvenance = false;
            foreach (var mesh in meshes)
            {
                // Check for mixed texture source provenance
                string? textureSource = null;
                if (!string.IsNullOrWhiteSpace(mesh.DiffuseTextureSource))
                    textureSource = mesh.DiffuseTextureSource;
                else if (mesh.DiffuseTexturePath is not null)
                    textureSource = "assimp";

                string samplingSource = mesh.SamplingControlsSource;

                // Mixed: texture and sampling come from different sources
                if (textureSource is not null &&
                    !string.Equals(textureSource, samplingSource, StringComparison.OrdinalIgnoreCase))
                {
                    if (!reportedMixedProvenance)
                    {
                        warnings.Add(new DiagnosticWarning
                        {
                            Code = "mixed_provenance_detected",
                            Message = "Mixed provenance: Unity sidecar and .vf-reference-settings.json (and/or manual overrides) each " +
                                      "contributed different parts of the material state for one or more meshes. See per-mesh diagnostics below.",
                            Severity = "info",
                        });
                        reportedMixedProvenance = true;
                    }

                    // Per-mesh detail
                    string meshLabel = string.IsNullOrWhiteSpace(mesh.MaterialName)
                        ? "unnamed"
                        : $"'{mesh.MaterialName}'";
                    string texturePart = textureSource switch
                    {
                        "unity_sidecar" => "diffuse texture from Unity .mat sidecar",
                        "vf_reference_settings" => "diffuse texture from .vf-reference-settings.json",
                        "manual_override" => "diffuse texture from manual override",
                        _ => $"diffuse source '{textureSource}'",
                    };
                    string samplingPart = samplingSource switch
                    {
                        "unity_sidecar" => "sampling controls from Unity .mat sidecar (default bottom_left)",
                        "vf_reference_settings" => "sampling controls from .vf-reference-settings.json",
                        "manual_sampling_override" => "sampling controls from manual override",
                        _ => $"sampling from '{samplingSource}'",
                    };
                    warnings.Add(new DiagnosticWarning
                    {
                        Code = "mixed_provenance_per_mesh",
                        Message = $"Mesh/mat {meshLabel}: {texturePart}, {samplingPart}.",
                        Severity = "info",
                    });
                }

                // Also check emissive texture source mixing
                if (!string.IsNullOrWhiteSpace(mesh.EmissiveTextureSource) &&
                    !string.Equals(mesh.EmissiveTextureSource, samplingSource, StringComparison.OrdinalIgnoreCase))
                {
                    string meshLabel2 = string.IsNullOrWhiteSpace(mesh.MaterialName)
                        ? "unnamed"
                        : $"'{mesh.MaterialName}'";
                    warnings.Add(new DiagnosticWarning
                    {
                        Code = "emissive_provenance_mix",
                        Message = $"Mesh/mat {meshLabel2}: emissive texture from {mesh.EmissiveTextureSource} while sampling is from {samplingSource}.",
                        Severity = "info",
                    });
                }
            }
        }
    }
}

public readonly record struct AabbResult
{
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }

    public Vector3 Size => Max - Min;
    public Vector3 Center => (Min + Max) / 2f;
    public float MaxDimension => MathF.Max(Size.X, MathF.Max(Size.Y, Size.Z));
}

public readonly record struct DiagnosticWarning
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
}

public readonly record struct MaterialSummary
{
    public int MaterialCount { get; init; }
    public IReadOnlyList<string> MaterialNames { get; init; }
    public int MeshesWithDiffuseTexture { get; init; }
    public int MeshesWithEmissiveTexture { get; init; }
}

public enum HeightAxis
{
    MaxDim,
    X,
    Y,
    Z,
}
