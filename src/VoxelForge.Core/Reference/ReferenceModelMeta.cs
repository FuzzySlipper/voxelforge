using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.Core.Reference;

/// <summary>
/// Serializable snapshot of a reference model's configuration.
/// Saved as a .refmeta JSON sidecar file.
/// </summary>
public sealed class ReferenceModelMeta
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public required string ModelPath { get; set; }

    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float Scale { get; set; } = 1f;

    public ReferenceRenderMode RenderMode { get; set; } = ReferenceRenderMode.Solid;
    public bool IsVisible { get; set; } = true;

    public List<MeshOverride>? MeshOverrides { get; set; }

    public AnimationSnapshot? Animation { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ReferenceModelMeta? FromJson(string json)
        => JsonSerializer.Deserialize<ReferenceModelMeta>(json, JsonOptions);

    /// <summary>
    /// Build a meta snapshot from a live model, with paths relative to the given directory.
    /// </summary>
    public static ReferenceModelMeta FromModel(ReferenceModelData model, string relativeToDir)
    {
        var meta = new ReferenceModelMeta
        {
            ModelPath = MakeRelative(model.FilePath, relativeToDir),
            PositionX = model.PositionX,
            PositionY = model.PositionY,
            PositionZ = model.PositionZ,
            RotationX = model.RotationX,
            RotationY = model.RotationY,
            RotationZ = model.RotationZ,
            Scale = model.Scale,
            RenderMode = model.RenderMode,
            IsVisible = model.IsVisible,
        };

        // Capture per-mesh texture overrides, manual overrides, and sampling controls
        var overrides = new List<MeshOverride>();
        for (int i = 0; i < model.Meshes.Count; i++)
        {
            var mesh = model.Meshes[i];
            if (mesh.DiffuseTexturePath is not null || mesh.EmissiveTexturePath is not null ||
                mesh.ManualDiffuseOverridePath is not null || mesh.ManualNormalOverridePath is not null ||
                mesh.ManualEmissiveOverridePath is not null ||
                mesh.SamplingControlsSource != "assimp")
            {
                var ov = new MeshOverride
                {
                    MeshIndex = i,
                    DiffuseTexturePath = mesh.DiffuseTexturePath is not null
                        ? MakeRelative(mesh.DiffuseTexturePath, relativeToDir) : null,
                    EmissiveTexturePath = mesh.EmissiveTexturePath is not null
                        ? MakeRelative(mesh.EmissiveTexturePath, relativeToDir) : null,
                    EmissiveBrightness = mesh.EmissiveBrightness > 0 ? mesh.EmissiveBrightness : null,
                };

                // Manual override paths
                if (mesh.ManualDiffuseOverridePath is not null)
                    ov.ManualDiffuseOverridePath = MakeRelative(mesh.ManualDiffuseOverridePath, relativeToDir);
                if (mesh.ManualNormalOverridePath is not null)
                    ov.ManualNormalOverridePath = MakeRelative(mesh.ManualNormalOverridePath, relativeToDir);
                if (mesh.ManualEmissiveOverridePath is not null)
                    ov.ManualEmissiveOverridePath = MakeRelative(mesh.ManualEmissiveOverridePath, relativeToDir);

                // Sampling controls (capture when non-default)
                if (mesh.UvOrigin != "top_left")
                    ov.UvOrigin = mesh.UvOrigin;
                if (mesh.FlipY != "asset_defined")
                    ov.FlipY = mesh.FlipY;
                if (mesh.WrapS != "repeat")
                    ov.WrapS = mesh.WrapS;
                if (mesh.WrapT != "repeat")
                    ov.WrapT = mesh.WrapT;
                if (mesh.SamplingControlsSource != "assimp")
                    ov.SamplingControlsSource = mesh.SamplingControlsSource;

                overrides.Add(ov);
            }
        }
        if (overrides.Count > 0)
            meta.MeshOverrides = overrides;

        // Capture animation snapshot
        if (model.HasAnimations)
        {
            meta.Animation = new AnimationSnapshot
            {
                ActiveClipIndex = model.ActiveClipIndex,
                Speed = model.AnimationSpeed,
            };
        }

        return meta;
    }

    /// <summary>
    /// Resolve all relative paths in this meta against the given base directory.
    /// </summary>
    public void ResolvePaths(string baseDir)
    {
        ModelPath = ResolveRelative(ModelPath, baseDir);
        if (MeshOverrides is not null)
        {
            foreach (var ov in MeshOverrides)
            {
                if (ov.DiffuseTexturePath is not null)
                    ov.DiffuseTexturePath = ResolveRelative(ov.DiffuseTexturePath, baseDir);
                if (ov.EmissiveTexturePath is not null)
                    ov.EmissiveTexturePath = ResolveRelative(ov.EmissiveTexturePath, baseDir);
                if (ov.ManualDiffuseOverridePath is not null)
                    ov.ManualDiffuseOverridePath = ResolveRelative(ov.ManualDiffuseOverridePath, baseDir);
                if (ov.ManualNormalOverridePath is not null)
                    ov.ManualNormalOverridePath = ResolveRelative(ov.ManualNormalOverridePath, baseDir);
                if (ov.ManualEmissiveOverridePath is not null)
                    ov.ManualEmissiveOverridePath = ResolveRelative(ov.ManualEmissiveOverridePath, baseDir);
            }
        }
    }

    private static string MakeRelative(string fullPath, string baseDir)
    {
        try
        {
            return Path.GetRelativePath(baseDir, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    private static string ResolveRelative(string path, string baseDir)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}

public sealed class MeshOverride
{
    public int MeshIndex { get; set; }

    /// <summary>Persistent diffuse texture path (from import or sidecar).</summary>
    public string? DiffuseTexturePath { get; set; }

    /// <summary>Persistent emissive texture path (from sidecar).</summary>
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

    // ── Sampling controls ──

    /// <summary>UV origin convention: "top_left", "bottom_left", "asset_defined".</summary>
    public string? UvOrigin { get; set; }

    /// <summary>Whether to flip V: "true", "false", "asset_defined".</summary>
    public string? FlipY { get; set; }

    /// <summary>Horizontal wrapping mode: "clamp", "repeat", "mirror".</summary>
    public string? WrapS { get; set; }

    /// <summary>Vertical wrapping mode: "clamp", "repeat", "mirror".</summary>
    public string? WrapT { get; set; }

    /// <summary>Provenance label for sampling controls: "assimp", "unity_sidecar", "manual_sampling_override".</summary>
    public string? SamplingControlsSource { get; set; }

    /// <summary>
    /// Apply this override's fields to a <see cref="ReferenceMeshData"/> instance.
    /// Only non-null mutable fields are applied, preserving existing values for omitted fields.
    /// Note: init-only properties (DiffuseTexturePath, EmissiveTexturePath, EmissiveBrightness)
    /// are excluded because they can only be set during object construction.
    /// Manual override paths and sampling controls are mutable and included here.
    /// </summary>
    public void ApplyToMesh(ReferenceMeshData mesh)
    {
        if (ManualDiffuseOverridePath is not null)
            mesh.ManualDiffuseOverridePath = ManualDiffuseOverridePath;
        if (ManualNormalOverridePath is not null)
            mesh.ManualNormalOverridePath = ManualNormalOverridePath;
        if (ManualEmissiveOverridePath is not null)
            mesh.ManualEmissiveOverridePath = ManualEmissiveOverridePath;
        if (UvOrigin is not null)
            mesh.UvOrigin = UvOrigin;
        if (FlipY is not null)
            mesh.FlipY = FlipY;
        if (WrapS is not null)
            mesh.WrapS = WrapS;
        if (WrapT is not null)
            mesh.WrapT = WrapT;
        if (SamplingControlsSource is not null)
            mesh.SamplingControlsSource = SamplingControlsSource;
    }
}

public sealed class AnimationSnapshot
{
    public int? ActiveClipIndex { get; set; }
    public float Speed { get; set; } = 1f;
}
