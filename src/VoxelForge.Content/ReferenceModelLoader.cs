using Assimp;
using Microsoft.Extensions.Logging;
using StbImageSharp;
using VoxelForge.Core.Reference;

namespace VoxelForge.Content;

/// <summary>
/// Loads 3D model files (FBX, OBJ, GLTF, etc.) via AssimpNetter.
/// Returns engine-agnostic mesh data.
/// </summary>
public sealed class ReferenceModelLoader
{
    private readonly ILogger<ReferenceModelLoader> _logger;

    public ReferenceModelLoader(ILogger<ReferenceModelLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inspects a model file and returns a diagnostic string describing
    /// materials, texture slots, and UV channel availability.
    /// </summary>
    public string Inspect(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Reference model not found: {filePath}");

        using var ctx = new AssimpContext();
        var scene = ctx.ImportFile(filePath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateNormals |
            PostProcessSteps.JoinIdenticalVertices);

        if (scene is null || scene.MeshCount == 0)
            return "No meshes found.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"File: {Path.GetFileName(filePath)}");
        sb.AppendLine($"Meshes: {scene.MeshCount}, Materials: {scene.MaterialCount}");
        sb.AppendLine();

        // Materials and texture slots
        for (int mi = 0; mi < scene.MaterialCount; mi++)
        {
            var mat = scene.Materials[mi];
            sb.AppendLine($"--- Material [{mi}] \"{mat.Name}\" ---");

            if (mat.HasColorDiffuse)
            {
                var c = mat.ColorDiffuse;
                sb.AppendLine($"  ColorDiffuse: ({c.X:F2}, {c.Y:F2}, {c.Z:F2}, {c.W:F2})");
            }
            if (mat.HasColorSpecular)
            {
                var c = mat.ColorSpecular;
                sb.AppendLine($"  ColorSpecular: ({c.X:F2}, {c.Y:F2}, {c.Z:F2}, {c.W:F2})");
            }
            if (mat.HasColorEmissive)
            {
                var c = mat.ColorEmissive;
                sb.AppendLine($"  ColorEmissive: ({c.X:F2}, {c.Y:F2}, {c.Z:F2}, {c.W:F2})");
            }

            var allTextures = mat.GetAllMaterialTextures().ToList();
            if (allTextures.Count > 0)
            {
                foreach (var tex in allTextures)
                    sb.AppendLine($"  Texture: type={tex.TextureType}, uvIndex={tex.UVIndex}, path=\"{tex.FilePath}\"");
            }
            else
            {
                sb.AppendLine("  (no texture slots)");
            }
        }

        sb.AppendLine();

        // Per-mesh UV and vertex color info
        for (int i = 0; i < scene.MeshCount; i++)
        {
            var mesh = scene.Meshes[i];
            int uvChannels = 0;
            for (int ch = 0; ch < 8; ch++)
            {
                if (mesh.HasTextureCoords(ch)) uvChannels++;
                else break;
            }

            sb.AppendLine($"Mesh [{i}] \"{mesh.Name}\": {mesh.VertexCount} verts, {mesh.FaceCount} faces, " +
                           $"materialIdx={mesh.MaterialIndex}, uvChannels={uvChannels}, hasVertexColors={mesh.HasVertexColors(0)}");
        }

        return sb.ToString();
    }

    public ReferenceModelData Load(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Reference model not found: {filePath}");

        using var ctx = new AssimpContext();
        var scene = ctx.ImportFile(filePath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateNormals |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.OptimizeMeshes);

        if (scene is null || scene.MeshCount == 0)
            throw new InvalidDataException($"No meshes found in {filePath}");

        var modelDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";

        // Cache loaded textures by resolved path so we don't load the same image twice
        var textureCache = new Dictionary<string, ImageResult>(StringComparer.OrdinalIgnoreCase);

        var meshes = new List<ReferenceMeshData>();
        foreach (var mesh in scene.Meshes)
            meshes.Add(ConvertMesh(mesh, scene, modelDir, textureCache));

        _logger.LogInformation("Loaded reference model {Path}: {MeshCount} meshes, {VertCount} vertices",
            filePath, meshes.Count, meshes.Sum(m => m.Vertices.Length));

        return new ReferenceModelData
        {
            FilePath = filePath,
            Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            Meshes = meshes,
        };
    }

    private ReferenceMeshData ConvertMesh(Mesh mesh, Scene scene, string modelDir,
        Dictionary<string, ImageResult> textureCache)
    {
        // Resolve material color if available
        byte matR = 180, matG = 180, matB = 180, matA = 255;
        string materialName = "default";
        string? diffuseTexturePath = null;
        ImageResult? diffuseImage = null;

        if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
        {
            var material = scene.Materials[mesh.MaterialIndex];
            materialName = material.Name ?? "default";

            if (material.HasColorDiffuse)
            {
                var dc = material.ColorDiffuse;
                matR = (byte)Math.Clamp((int)(dc.X * 255), 0, 255);
                matG = (byte)Math.Clamp((int)(dc.Y * 255), 0, 255);
                matB = (byte)Math.Clamp((int)(dc.Z * 255), 0, 255);
                matA = (byte)Math.Clamp((int)(dc.W * 255), 0, 255);
            }

            // Try to find and load the diffuse texture
            diffuseTexturePath = ResolveDiffuseTexture(material, materialName, modelDir);
            if (diffuseTexturePath is not null)
            {
                if (!textureCache.TryGetValue(diffuseTexturePath, out diffuseImage))
                {
                    diffuseImage = LoadImage(diffuseTexturePath);
                    if (diffuseImage is not null)
                        textureCache[diffuseTexturePath] = diffuseImage;
                }
            }
        }

        bool hasUvs = mesh.HasTextureCoords(0);

        var vertices = new ReferenceVertex[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var pos = mesh.Vertices[i];
            float nx = 0, ny = 1, nz = 0;
            if (mesh.HasNormals)
            {
                nx = mesh.Normals[i].X;
                ny = mesh.Normals[i].Y;
                nz = mesh.Normals[i].Z;
            }

            float u = 0, v = 0;
            if (hasUvs)
            {
                var uv = mesh.TextureCoordinateChannels[0][i];
                u = uv.X;
                v = uv.Y;
            }

            byte r = matR, g = matG, b = matB, a = matA;

            // If we have a diffuse texture and UVs, sample the texture to bake color
            if (diffuseImage is not null && hasUvs)
            {
                SampleTexture(diffuseImage, u, v, out r, out g, out b, out a);
            }
            else if (mesh.HasVertexColors(0))
            {
                var vc = mesh.VertexColorChannels[0][i];
                r = (byte)Math.Clamp((int)(vc.X * 255), 0, 255);
                g = (byte)Math.Clamp((int)(vc.Y * 255), 0, 255);
                b = (byte)Math.Clamp((int)(vc.Z * 255), 0, 255);
                a = (byte)Math.Clamp((int)(vc.W * 255), 0, 255);
            }

            vertices[i] = new ReferenceVertex(pos.X, pos.Y, pos.Z, nx, ny, nz, r, g, b, a, u, v);
        }

        var indices = new List<int>();
        foreach (var face in mesh.Faces)
        {
            foreach (var idx in face.Indices)
                indices.Add(idx);
        }

        return new ReferenceMeshData
        {
            Vertices = vertices,
            Indices = indices.ToArray(),
            MaterialName = materialName,
            DiffuseTexturePath = diffuseTexturePath,
        };
    }

    /// <summary>
    /// Tries to find the diffuse texture on disk. First checks Assimp's texture path,
    /// then falls back to pattern matching by material name in the model directory.
    /// </summary>
    private string? ResolveDiffuseTexture(Material material, string materialName, string modelDir)
    {
        // 1. Try the path Assimp reports for the diffuse texture slot
        var diffuseSlots = material.GetMaterialTextures(TextureType.Diffuse);
        if (diffuseSlots.Length > 0)
        {
            var rawPath = diffuseSlots[0].FilePath;
            if (!string.IsNullOrWhiteSpace(rawPath))
            {
                // Try as-is relative to model dir
                var candidate = Path.Combine(modelDir, rawPath);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                // Try just the filename
                var filename = Path.GetFileName(rawPath);
                candidate = Path.Combine(modelDir, filename);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                // Try swapping extension to common image formats
                var baseName = Path.GetFileNameWithoutExtension(rawPath);
                foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" })
                {
                    candidate = Path.Combine(modelDir, baseName + ext);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
        }

        // 2. Fall back to pattern matching: look for {MaterialName}_Base_Color.* or {MaterialName}_diffuse.*
        foreach (var pattern in new[] { "_Base_Color", "_diffuse", "_Diffuse", "_basecolor", "_albedo", "_Albedo" })
        {
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" })
            {
                var candidate = Path.Combine(modelDir, materialName + pattern + ext);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        // 3. Try just {MaterialName}.png etc. (some models name the diffuse after the material)
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" })
        {
            var candidate = Path.Combine(modelDir, materialName + ext);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        _logger.LogWarning("No diffuse texture found for material '{Material}' in {Dir}", materialName, modelDir);
        return null;
    }

    private ImageResult? LoadImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            _logger.LogInformation("Loaded texture {Path}: {W}x{H}", path, image.Width, image.Height);
            return image;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load texture {Path}", path);
            return null;
        }
    }

    private static void SampleTexture(ImageResult image, float u, float v,
        out byte r, out byte g, out byte b, out byte a)
    {
        // Wrap UVs to [0,1) range (handles tiling)
        u = u - MathF.Floor(u);
        v = v - MathF.Floor(v);

        // Flip V — most 3D formats use bottom-left origin, images use top-left
        v = 1f - v;

        int px = Math.Clamp((int)(u * image.Width), 0, image.Width - 1);
        int py = Math.Clamp((int)(v * image.Height), 0, image.Height - 1);

        int offset = (py * image.Width + px) * 4; // RGBA
        r = image.Data[offset];
        g = image.Data[offset + 1];
        b = image.Data[offset + 2];
        a = image.Data[offset + 3];
    }
}
