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

        // Capture per-mesh texture overrides
        var overrides = new List<MeshOverride>();
        for (int i = 0; i < model.Meshes.Count; i++)
        {
            var mesh = model.Meshes[i];
            if (mesh.DiffuseTexturePath is not null || mesh.EmissiveTexturePath is not null)
            {
                overrides.Add(new MeshOverride
                {
                    MeshIndex = i,
                    DiffuseTexturePath = mesh.DiffuseTexturePath is not null
                        ? MakeRelative(mesh.DiffuseTexturePath, relativeToDir) : null,
                    EmissiveTexturePath = mesh.EmissiveTexturePath is not null
                        ? MakeRelative(mesh.EmissiveTexturePath, relativeToDir) : null,
                    EmissiveBrightness = mesh.EmissiveBrightness > 0 ? mesh.EmissiveBrightness : null,
                });
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
    public string? DiffuseTexturePath { get; set; }
    public string? EmissiveTexturePath { get; set; }
    public float? EmissiveBrightness { get; set; }
}

public sealed class AnimationSnapshot
{
    public int? ActiveClipIndex { get; set; }
    public float Speed { get; set; } = 1f;
}
