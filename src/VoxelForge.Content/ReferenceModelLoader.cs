using Assimp;
using Microsoft.Extensions.Logging;
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

        var meshes = new List<ReferenceMeshData>();
        foreach (var mesh in scene.Meshes)
            meshes.Add(ConvertMesh(mesh, scene));

        _logger.LogInformation("Loaded reference model {Path}: {MeshCount} meshes, {VertCount} vertices",
            filePath, meshes.Count, meshes.Sum(m => m.Vertices.Length));

        return new ReferenceModelData
        {
            FilePath = filePath,
            Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
            Meshes = meshes,
        };
    }

    private static ReferenceMeshData ConvertMesh(Mesh mesh, Scene scene)
    {
        // Resolve material color if available
        byte matR = 180, matG = 180, matB = 180, matA = 255;
        string materialName = "default";

        if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
        {
            var material = scene.Materials[mesh.MaterialIndex];
            materialName = material.Name ?? "default";

            if (material.HasColorDiffuse)
            {
                var dc = material.ColorDiffuse;
                matR = (byte)(dc.X * 255);
                matG = (byte)(dc.Y * 255);
                matB = (byte)(dc.Z * 255);
                matA = (byte)(dc.W * 255);
            }
        }

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

            byte r = matR, g = matG, b = matB, a = matA;
            if (mesh.HasVertexColors(0))
            {
                var vc = mesh.VertexColorChannels[0][i];
                r = (byte)(vc.X * 255);
                g = (byte)(vc.Y * 255);
                b = (byte)(vc.Z * 255);
                a = (byte)(vc.W * 255);
            }

            vertices[i] = new ReferenceVertex(pos.X, pos.Y, pos.Z, nx, ny, nz, r, g, b, a);
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
        };
    }
}
