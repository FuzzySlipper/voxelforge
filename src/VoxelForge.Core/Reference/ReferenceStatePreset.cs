using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.Core.Reference;

/// <summary>
/// Versioned, durable JSON schema for exporting/importing all loaded reference
/// model state as a single reloadable bundle. This is a multi-model preset,
/// distinct from the per-model <see cref="ReferenceModelMeta"/> sidecar.
///
/// Schema version history:
///   V1 — initial: source paths (absolute only), transforms, per-mesh texture
///         overrides (diffuse/normal/emissive), texture sampling controls,
///         visibility, render mode, animation state, and provenance notes.
///         Project-relative path resolution is a documented V1 limitation.
/// </summary>
public sealed class ReferenceStatePreset
{
    /// <summary>Current schema version. Increment on breaking changes.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // ── Envelope ──

    /// <summary>Schema version for forward/backward compatibility.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Human-readable label for this preset.</summary>
    public string? Label { get; set; }

    /// <summary>Provenance notes, workflow context, or agent instructions.</summary>
    public string? Notes { get; set; }

    /// <summary>ISO 8601 timestamp of preset creation.</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>Tool or agent that created this preset.</summary>
    public string CreatedBy { get; set; } = "VoxelForge";

    /// <summary>The reference model entries in this preset.</summary>
    public List<ReferenceStatePresetEntry> Entries { get; set; } = [];

    // ── Serialization ──

    /// <summary>Serialize this preset to indented JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserialize a preset from JSON. Returns null on parse failure or wrong version.</summary>
    public static ReferenceStatePreset? FromJson(string json)
    {
        try
        {
            var preset = JsonSerializer.Deserialize<ReferenceStatePreset>(json, JsonOptions);
            if (preset is null)
                return null;
            if (preset.SchemaVersion != CurrentSchemaVersion)
                return null;
            return preset;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates schema version and returns a human-readable error if incompatible.
    /// </summary>
    public static bool TryValidateSchema(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("schemaVersion", out var versionEl))
            {
                error = "Missing 'schemaVersion' field.";
                return false;
            }

            if (versionEl.ValueKind != JsonValueKind.Number || !versionEl.TryGetInt32(out int version))
            {
                error = "'schemaVersion' must be an integer.";
                return false;
            }

            if (version != CurrentSchemaVersion)
            {
                error = $"Incompatible schema version {version}. Current version is {CurrentSchemaVersion}.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    // ── Build from live model data (Core-only, no ReferenceModelState dependency) ──

    /// <summary>
    /// Build a preset from a list of <see cref="ReferenceModelData"/> instances.
    /// Source paths are stored as absolute paths (project-relative resolution
    /// is a documented V1 limitation).
    /// </summary>
    public static ReferenceStatePreset FromModels(
        IReadOnlyList<ReferenceModelData> models,
        string? label = null,
        string? notes = null,
        string? createdBy = null)
    {
        ArgumentNullException.ThrowIfNull(models);

        var preset = new ReferenceStatePreset
        {
            Label = label,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy ?? "VoxelForge",
        };

        foreach (var model in models)
        {
            var entry = new ReferenceStatePresetEntry
            {
                SourcePath = model.FilePath,
                Format = model.Format,
                PositionX = model.PositionX,
                PositionY = model.PositionY,
                PositionZ = model.PositionZ,
                RotationX = model.RotationX,
                RotationY = model.RotationY,
                RotationZ = model.RotationZ,
                Scale = model.Scale,
                IsVisible = model.IsVisible,
                RenderMode = model.RenderMode.ToString().ToLowerInvariant(),
                ActiveClipIndex = model.ActiveClipIndex,
                AnimationSpeed = model.AnimationSpeed,
                HasAnimations = model.HasAnimations,
            };

            // Capture per-mesh state where it differs from defaults
            var meshOverrides = new List<PresetMeshOverride>();
            for (int mi = 0; mi < model.Meshes.Count; mi++)
            {
                var mesh = model.Meshes[mi];
                var hasOverrides = false;
                var ov = new PresetMeshOverride
                {
                    MeshIndex = mi,
                };

                // Import source texture paths
                if (mesh.DiffuseTexturePath is not null)
                {
                    ov.DiffuseTexturePath = mesh.DiffuseTexturePath;
                    hasOverrides = true;
                }
                if (mesh.EmissiveTexturePath is not null)
                {
                    ov.EmissiveTexturePath = mesh.EmissiveTexturePath;
                    hasOverrides = true;
                }
                if (mesh.EmissiveBrightness > 0f)
                {
                    ov.EmissiveBrightness = mesh.EmissiveBrightness;
                    hasOverrides = true;
                }

                // Manual override paths
                if (mesh.ManualDiffuseOverridePath is not null)
                {
                    ov.ManualDiffuseOverridePath = mesh.ManualDiffuseOverridePath;
                    hasOverrides = true;
                }
                if (mesh.ManualNormalOverridePath is not null)
                {
                    ov.ManualNormalOverridePath = mesh.ManualNormalOverridePath;
                    hasOverrides = true;
                }
                if (mesh.ManualEmissiveOverridePath is not null)
                {
                    ov.ManualEmissiveOverridePath = mesh.ManualEmissiveOverridePath;
                    hasOverrides = true;
                }

                // Sampling controls — capture when non-default
                if (mesh.UvOrigin != "top_left")
                {
                    ov.UvOrigin = mesh.UvOrigin;
                    hasOverrides = true;
                }
                if (mesh.FlipY != "asset_defined")
                {
                    ov.FlipY = mesh.FlipY;
                    hasOverrides = true;
                }
                if (mesh.WrapS != "repeat")
                {
                    ov.WrapS = mesh.WrapS;
                    hasOverrides = true;
                }
                if (mesh.WrapT != "repeat")
                {
                    ov.WrapT = mesh.WrapT;
                    hasOverrides = true;
                }
                if (mesh.SamplingControlsSource != "assimp")
                {
                    ov.SamplingControlsSource = mesh.SamplingControlsSource;
                    hasOverrides = true;
                }

                // Material name — capture when non-default
                if (mesh.MaterialName != "default")
                {
                    ov.MaterialName = mesh.MaterialName;
                    hasOverrides = true;
                }

                if (hasOverrides)
                    meshOverrides.Add(ov);
            }

            if (meshOverrides.Count > 0)
                entry.MeshOverrides = meshOverrides;

            preset.Entries.Add(entry);
        }

        return preset;
    }
}

/// <summary>
/// A single reference model entry within a <see cref="ReferenceStatePreset"/>.
/// Contains all state needed to reload and restore the model.
/// </summary>
public sealed class ReferenceStatePresetEntry
{
    // ── Source ──

    /// <summary>
    /// Path to the source 3D model file. Absolute paths are preferred and fully
    /// supported. Project-relative path resolution is a documented V1 gap.
    /// </summary>
    public required string SourcePath { get; set; }

    /// <summary>File format hint (e.g. "FBX", "OBJ", "GLTF").</summary>
    public string? Format { get; set; }

    /// <summary>Label from the import/source pipeline (e.g. "assimp", "unity_sidecar").</summary>
    public string? ImportSourceLabel { get; set; }

    // ── Transform ──

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float Scale { get; set; } = 1f;

    // ── Visibility and render mode ──

    public bool IsVisible { get; set; } = true;

    /// <summary>Render mode string: "solid", "wireframe", "transparent".</summary>
    public string RenderMode { get; set; } = "solid";

    // ── Animation state ──

    public int? ActiveClipIndex { get; set; }
    public float AnimationSpeed { get; set; } = 1f;
    public bool HasAnimations { get; set; }

    // ── Per-mesh overrides ──

    public List<PresetMeshOverride>? MeshOverrides { get; set; }

    // ── Provenance ──

    /// <summary>Free-form provenance or workflow context for this entry.</summary>
    public string? Provenance { get; set; }
}

/// <summary>
/// Per-mesh state captured in a <see cref="ReferenceStatePreset"/>.
/// Mirrors <see cref="MeshOverride"/> from the per-model .refmeta system
/// but uses absolute paths (no relative path logic) and includes
/// additional fields for full state fidelity.
/// </summary>
public sealed class PresetMeshOverride
{
    public int MeshIndex { get; set; }

    public string? MaterialName { get; set; }

    // ── Import/source texture paths ──

    /// <summary>Diffuse texture path from import or sidecar resolution.</summary>
    public string? DiffuseTexturePath { get; set; }

    /// <summary>Emissive texture path from import or sidecar resolution.</summary>
    public string? EmissiveTexturePath { get; set; }

    /// <summary>Emissive brightness multiplier.</summary>
    public float? EmissiveBrightness { get; set; }

    // ── Session manual override paths (persisted for reload) ──

    /// <summary>Manual override for diffuse texture. Null if not set.</summary>
    public string? ManualDiffuseOverridePath { get; set; }

    /// <summary>Manual override for normal map texture. Null if not set.</summary>
    public string? ManualNormalOverridePath { get; set; }

    /// <summary>Manual override for emissive texture. Null if not set.</summary>
    public string? ManualEmissiveOverridePath { get; set; }

    // ── Texture sampling controls ──

    /// <summary>UV origin convention: "top_left", "bottom_left", "asset_defined".</summary>
    public string? UvOrigin { get; set; }

    /// <summary>Whether to flip V: "true", "false", "asset_defined".</summary>
    public string? FlipY { get; set; }

    /// <summary>Horizontal wrapping mode: "clamp", "repeat", "mirror".</summary>
    public string? WrapS { get; set; }

    /// <summary>Vertical wrapping mode: "clamp", "repeat", "mirror".</summary>
    public string? WrapT { get; set; }

    /// <summary>Provenance label: "assimp", "unity_sidecar", "manual_sampling_override", "vf_reference_settings".</summary>
    public string? SamplingControlsSource { get; set; }
}
