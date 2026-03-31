using System.Numerics;
using Assimp;
using Microsoft.Extensions.Logging;
using StbImageSharp;
using VoxelForge.Core.Reference;

using SysQuaternion = System.Numerics.Quaternion;
using SysMatrix = System.Numerics.Matrix4x4;
using SysVector3 = System.Numerics.Vector3;

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
            PostProcessSteps.OptimizeMeshes |
            PostProcessSteps.LimitBoneWeights);

        if (scene is null || scene.MeshCount == 0)
            throw new InvalidDataException($"No meshes found in {filePath}");

        var modelDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";

        // Build skeleton from node hierarchy if any mesh has bones
        bool hasBones = scene.Meshes.Any(m => m.HasBones);
        Skeleton? skeleton = null;
        Dictionary<string, int>? boneNameToIndex = null;

        if (hasBones && scene.RootNode is not null)
        {
            (skeleton, boneNameToIndex) = BuildSkeleton(scene);
            _logger.LogInformation("Extracted skeleton: {BoneCount} bones", skeleton.BoneCount);
        }

        // Cache loaded textures by resolved path so we don't load the same image twice
        var textureCache = new Dictionary<string, ImageResult>(StringComparer.OrdinalIgnoreCase);

        var meshes = new List<ReferenceMeshData>();
        foreach (var mesh in scene.Meshes)
            meshes.Add(ConvertMesh(mesh, scene, modelDir, textureCache, boneNameToIndex));

        // Extract animation clips
        List<SkeletalAnimationClip>? clips = null;
        if (skeleton is not null && scene.HasAnimations)
        {
            clips = ExtractAnimations(scene, boneNameToIndex!);
            _logger.LogInformation("Extracted {ClipCount} animation clips", clips.Count);
        }

        _logger.LogInformation("Loaded reference model {Path}: {MeshCount} meshes, {VertCount} vertices",
            filePath, meshes.Count, meshes.Sum(m => m.Vertices.Length));

        return new ReferenceModelData
        {
            FilePath = filePath,
            Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            Meshes = meshes,
            Skeleton = skeleton,
            AnimationClips = clips,
        };
    }

    private ReferenceMeshData ConvertMesh(Mesh mesh, Scene scene, string modelDir,
        Dictionary<string, ImageResult> textureCache,
        Dictionary<string, int>? boneNameToIndex = null)
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

        // Extract bone weights per vertex (up to 4 influences, limited by Assimp post-process)
        if (mesh.HasBones && boneNameToIndex is not null)
        {
            // Accumulate bone influences per vertex
            var boneIndices = new int[mesh.VertexCount * 4];
            var boneWeights = new float[mesh.VertexCount * 4];
            var influenceCount = new int[mesh.VertexCount];

            foreach (var bone in mesh.Bones)
            {
                if (!boneNameToIndex.TryGetValue(bone.Name, out int boneIdx))
                    continue;

                if (bone.HasVertexWeights)
                {
                    foreach (var vw in bone.VertexWeights)
                    {
                        int vi = vw.VertexID;
                        int slot = influenceCount[vi];
                        if (slot < 4)
                        {
                            int offset = vi * 4 + slot;
                            boneIndices[offset] = boneIdx;
                            boneWeights[offset] = vw.Weight;
                            influenceCount[vi] = slot + 1;
                        }
                    }
                }
            }

            // Rebuild vertices with bone data
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int off = i * 4;
                var v = vertices[i];
                vertices[i] = new ReferenceVertex(
                    v.PosX, v.PosY, v.PosZ,
                    v.NormX, v.NormY, v.NormZ,
                    v.R, v.G, v.B, v.A,
                    v.U, v.V,
                    boneIndices[off], boneIndices[off + 1], boneIndices[off + 2], boneIndices[off + 3],
                    boneWeights[off], boneWeights[off + 1], boneWeights[off + 2], boneWeights[off + 3]);
            }
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

    /// <summary>
    /// Builds a flat bone list from the Assimp node hierarchy.
    /// Includes all nodes that are referenced as bones by any mesh, plus their ancestors
    /// up to the root so the hierarchy is intact.
    /// </summary>
    private (Skeleton skeleton, Dictionary<string, int> nameToIndex) BuildSkeleton(Scene scene)
    {
        // Collect all bone names from all meshes
        var boneNames = new HashSet<string>();
        var inverseBindMatrices = new Dictionary<string, SysMatrix>();
        foreach (var mesh in scene.Meshes)
        {
            if (!mesh.HasBones) continue;
            foreach (var bone in mesh.Bones)
            {
                boneNames.Add(bone.Name);
                inverseBindMatrices[bone.Name] = bone.OffsetMatrix;
            }
        }

        // Walk the node tree depth-first, collecting nodes that are bones or ancestors of bones
        var needed = new HashSet<string>(boneNames);
        MarkAncestors(scene.RootNode, needed);

        var bones = new List<Core.Reference.Bone>();
        var nameToIndex = new Dictionary<string, int>();
        FlattenNodeTree(scene.RootNode, -1, needed, inverseBindMatrices, bones, nameToIndex);

        return (new Skeleton { Bones = bones, RootIndex = 0 }, nameToIndex);
    }

    /// <summary>
    /// Marks all ancestor nodes of bone nodes as needed so the hierarchy is complete.
    /// Returns true if any descendant is a bone.
    /// </summary>
    private static bool MarkAncestors(Node node, HashSet<string> needed)
    {
        bool childNeeded = false;
        if (node.HasChildren)
        {
            foreach (var child in node.Children)
            {
                if (MarkAncestors(child, needed))
                    childNeeded = true;
            }
        }

        if (childNeeded)
            needed.Add(node.Name);

        return childNeeded || needed.Contains(node.Name);
    }

    private static void FlattenNodeTree(Node node, int parentIndex,
        HashSet<string> needed, Dictionary<string, SysMatrix> inverseBindMatrices,
        List<Core.Reference.Bone> bones, Dictionary<string, int> nameToIndex)
    {
        if (!needed.Contains(node.Name) && node.Parent is not null)
            return; // Skip nodes that aren't part of the skeleton hierarchy

        int myIndex = bones.Count;
        nameToIndex[node.Name] = myIndex;

        inverseBindMatrices.TryGetValue(node.Name, out var ibm);

        var t = node.Transform;
        bones.Add(new Core.Reference.Bone
        {
            Name = node.Name,
            ParentIndex = parentIndex,
            InverseBindMatrix = ibm.Equals(default(SysMatrix))
                ? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
                : Mat4ToArray(ibm),
            LocalBindTransform = Mat4ToArray(t),
        });

        if (node.HasChildren)
        {
            foreach (var child in node.Children)
                FlattenNodeTree(child, myIndex, needed, inverseBindMatrices, bones, nameToIndex);
        }
    }

    /// <summary>
    /// Extracts all animation clips from the Assimp scene.
    /// </summary>
    private List<SkeletalAnimationClip> ExtractAnimations(Scene scene, Dictionary<string, int> boneNameToIndex)
    {
        var clips = new List<SkeletalAnimationClip>();

        foreach (var anim in scene.Animations)
        {
            float tps = anim.TicksPerSecond > 0 ? (float)anim.TicksPerSecond : 25f;
            float duration = (float)(anim.DurationInTicks / tps);

            var channels = new List<BoneAnimationChannel>();

            if (anim.HasNodeAnimations)
            {
                foreach (var channel in anim.NodeAnimationChannels)
                {
                    if (!boneNameToIndex.TryGetValue(channel.NodeName, out int boneIdx))
                        continue;

                    // Merge all keyframe times from position, rotation, and scale tracks
                    var times = new SortedSet<float>();
                    if (channel.HasPositionKeys)
                        foreach (var k in channel.PositionKeys) times.Add((float)(k.Time / tps));
                    if (channel.HasRotationKeys)
                        foreach (var k in channel.RotationKeys) times.Add((float)(k.Time / tps));
                    if (channel.HasScalingKeys)
                        foreach (var k in channel.ScalingKeys) times.Add((float)(k.Time / tps));

                    var keyframes = new List<BoneKeyframe>();
                    foreach (float t in times)
                    {
                        float tickTime = t * tps;

                        // Sample position at this time
                        var pos = SamplePosition(channel, tickTime);
                        var rot = SampleRotation(channel, tickTime);
                        var scl = SampleScale(channel, tickTime);

                        keyframes.Add(new BoneKeyframe(t,
                            pos.X, pos.Y, pos.Z,
                            rot.X, rot.Y, rot.Z, rot.W,
                            scl.X, scl.Y, scl.Z));
                    }

                    channels.Add(new BoneAnimationChannel
                    {
                        BoneIndex = boneIdx,
                        BoneName = channel.NodeName,
                        Keyframes = keyframes.ToArray(),
                    });
                }
            }

            clips.Add(new SkeletalAnimationClip
            {
                Name = string.IsNullOrWhiteSpace(anim.Name) ? $"Clip{clips.Count}" : anim.Name,
                Duration = duration,
                TicksPerSecond = tps,
                Channels = channels,
            });

            _logger.LogInformation("Animation '{Name}': {Duration:F2}s, {Channels} channels",
                clips[^1].Name, duration, channels.Count);
        }

        return clips;
    }

    private static SysVector3 SamplePosition(NodeAnimationChannel channel, float tick)
    {
        if (!channel.HasPositionKeys || channel.PositionKeyCount == 0)
            return SysVector3.Zero;
        if (channel.PositionKeyCount == 1)
            return channel.PositionKeys[0].Value;

        for (int i = channel.PositionKeyCount - 2; i >= 0; i--)
        {
            if (channel.PositionKeys[i].Time <= tick)
            {
                var k0 = channel.PositionKeys[i];
                var k1 = channel.PositionKeys[i + 1];
                float dt = (float)(k1.Time - k0.Time);
                float f = dt > 0 ? (float)((tick - k0.Time) / dt) : 0;
                return SysVector3.Lerp(k0.Value, k1.Value, f);
            }
        }
        return channel.PositionKeys[0].Value;
    }

    private static SysQuaternion SampleRotation(NodeAnimationChannel channel, float tick)
    {
        if (!channel.HasRotationKeys || channel.RotationKeyCount == 0)
            return SysQuaternion.Identity;
        if (channel.RotationKeyCount == 1)
            return channel.RotationKeys[0].Value;

        for (int i = channel.RotationKeyCount - 2; i >= 0; i--)
        {
            if (channel.RotationKeys[i].Time <= tick)
            {
                var k0 = channel.RotationKeys[i];
                var k1 = channel.RotationKeys[i + 1];
                float dt = (float)(k1.Time - k0.Time);
                float f = dt > 0 ? (float)((tick - k0.Time) / dt) : 0;
                return SysQuaternion.Slerp(k0.Value, k1.Value, f);
            }
        }
        return channel.RotationKeys[0].Value;
    }

    private static SysVector3 SampleScale(NodeAnimationChannel channel, float tick)
    {
        if (!channel.HasScalingKeys || channel.ScalingKeyCount == 0)
            return SysVector3.One;
        if (channel.ScalingKeyCount == 1)
            return channel.ScalingKeys[0].Value;

        for (int i = channel.ScalingKeyCount - 2; i >= 0; i--)
        {
            if (channel.ScalingKeys[i].Time <= tick)
            {
                var k0 = channel.ScalingKeys[i];
                var k1 = channel.ScalingKeys[i + 1];
                float dt = (float)(k1.Time - k0.Time);
                float f = dt > 0 ? (float)((tick - k0.Time) / dt) : 0;
                return SysVector3.Lerp(k0.Value, k1.Value, f);
            }
        }
        return channel.ScalingKeys[0].Value;
    }

    private static float[] Mat4ToArray(SysMatrix m) =>
    [
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44,
    ];

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

    /// <summary>
    /// Re-bake a mesh's vertex colors from a new diffuse texture using existing UVs.
    /// If the mesh has an emissive texture, it is re-applied on top.
    /// Returns a new ReferenceMeshData with updated colors, or null on failure.
    /// </summary>
    public ReferenceMeshData? Retexture(ReferenceMeshData mesh, string texturePath)
    {
        var diffuseImage = LoadImage(texturePath);
        if (diffuseImage is null)
        {
            _logger.LogError("Failed to load texture for retexture: {Path}", texturePath);
            return null;
        }

        ImageResult? emissiveImage = null;
        if (mesh.EmissiveTexturePath is not null)
            emissiveImage = LoadImage(mesh.EmissiveTexturePath);

        var newVerts = BakeVertexColors(mesh.Vertices, diffuseImage, emissiveImage, mesh.EmissiveBrightness);

        return new ReferenceMeshData
        {
            Vertices = newVerts,
            Indices = mesh.Indices,
            MaterialName = mesh.MaterialName,
            DiffuseTexturePath = texturePath,
            EmissiveTexturePath = mesh.EmissiveTexturePath,
            EmissiveBrightness = mesh.EmissiveBrightness,
        };
    }

    /// <summary>
    /// Apply an emissive texture to a mesh, blending it into existing vertex colors
    /// with the given brightness multiplier. Re-bakes from diffuse first if available.
    /// Returns a new ReferenceMeshData, or null on failure.
    /// </summary>
    public ReferenceMeshData? RetextureEmissive(ReferenceMeshData mesh, string emissivePath, float brightness)
    {
        var emissiveImage = LoadImage(emissivePath);
        if (emissiveImage is null)
        {
            _logger.LogError("Failed to load emissive texture: {Path}", emissivePath);
            return null;
        }

        // Re-bake from diffuse if we have a path, otherwise use current vertex colors as base.
        ImageResult? diffuseImage = null;
        if (mesh.DiffuseTexturePath is not null)
            diffuseImage = LoadImage(mesh.DiffuseTexturePath);

        ReferenceVertex[] newVerts;
        if (diffuseImage is not null)
        {
            newVerts = BakeVertexColors(mesh.Vertices, diffuseImage, emissiveImage, brightness);
        }
        else
        {
            // No diffuse texture — blend emissive into existing vertex colors.
            newVerts = BlendEmissive(mesh.Vertices, emissiveImage, brightness);
        }

        return new ReferenceMeshData
        {
            Vertices = newVerts,
            Indices = mesh.Indices,
            MaterialName = mesh.MaterialName,
            DiffuseTexturePath = mesh.DiffuseTexturePath,
            EmissiveTexturePath = emissivePath,
            EmissiveBrightness = brightness,
        };
    }

    private ReferenceVertex[] BakeVertexColors(
        ReferenceVertex[] oldVerts, ImageResult diffuse, ImageResult? emissive, float emissiveBrightness)
    {
        var newVerts = new ReferenceVertex[oldVerts.Length];
        for (int i = 0; i < oldVerts.Length; i++)
        {
            var ov = oldVerts[i];
            SampleTexture(diffuse, ov.U, ov.V, out byte r, out byte g, out byte b, out byte a);

            if (emissive is not null && emissiveBrightness > 0f)
            {
                SampleTexture(emissive, ov.U, ov.V, out byte er, out byte eg, out byte eb, out _);
                r = (byte)Math.Clamp(r + (int)(er * emissiveBrightness), 0, 255);
                g = (byte)Math.Clamp(g + (int)(eg * emissiveBrightness), 0, 255);
                b = (byte)Math.Clamp(b + (int)(eb * emissiveBrightness), 0, 255);
            }

            newVerts[i] = new ReferenceVertex(
                ov.PosX, ov.PosY, ov.PosZ,
                ov.NormX, ov.NormY, ov.NormZ,
                r, g, b, a,
                ov.U, ov.V,
                ov.BoneIndex0, ov.BoneIndex1, ov.BoneIndex2, ov.BoneIndex3,
                ov.BoneWeight0, ov.BoneWeight1, ov.BoneWeight2, ov.BoneWeight3);
        }
        return newVerts;
    }

    private ReferenceVertex[] BlendEmissive(
        ReferenceVertex[] oldVerts, ImageResult emissive, float brightness)
    {
        var newVerts = new ReferenceVertex[oldVerts.Length];
        for (int i = 0; i < oldVerts.Length; i++)
        {
            var ov = oldVerts[i];
            SampleTexture(emissive, ov.U, ov.V, out byte er, out byte eg, out byte eb, out _);
            byte r = (byte)Math.Clamp(ov.R + (int)(er * brightness), 0, 255);
            byte g = (byte)Math.Clamp(ov.G + (int)(eg * brightness), 0, 255);
            byte b = (byte)Math.Clamp(ov.B + (int)(eb * brightness), 0, 255);

            newVerts[i] = new ReferenceVertex(
                ov.PosX, ov.PosY, ov.PosZ,
                ov.NormX, ov.NormY, ov.NormZ,
                r, g, b, ov.A,
                ov.U, ov.V,
                ov.BoneIndex0, ov.BoneIndex1, ov.BoneIndex2, ov.BoneIndex3,
                ov.BoneWeight0, ov.BoneWeight1, ov.BoneWeight2, ov.BoneWeight3);
        }
        return newVerts;
    }
}
