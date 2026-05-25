using VoxelForge.App.Render;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service that produces canonical versioned <see cref="RenderSceneSnapshot"/>
/// from workspace state and existing mesh/palette/reference services.
/// <para>
/// This service does NOT hold mutable state. It operates over explicit state arguments.
/// Transport hosts (MCP, Bridge) call this service and serialize the result.
/// </para>
/// </summary>
public sealed class RenderSceneSnapshotService
{
    private readonly MeshSnapshotService _meshService;
    private readonly PaletteSnapshotService _paletteService;

    public RenderSceneSnapshotService(
        MeshSnapshotService meshService,
        PaletteSnapshotService paletteService)
    {
        ArgumentNullException.ThrowIfNull(meshService);
        ArgumentNullException.ThrowIfNull(paletteService);
        _meshService = meshService;
        _paletteService = paletteService;
    }

    /// <summary>
    /// Build a canonical render-scene snapshot from the given workspace state.
    /// </summary>
    public RenderSceneSnapshot BuildSnapshot(
        VoxelForgeWorkspaceState workspace,
        string hostId = "test",
        IReadOnlyList<string>? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var mesh = _meshService.BuildSnapshot(workspace.Document.Model);
        var palette = _paletteService.BuildSnapshot(workspace.Document.Model.Palette);

        // Voxel bounds from mesh
        BoundsDto? voxelBounds = mesh.Bounds is { } b
            ? new BoundsDto
            {
                MinX = b.MinX, MinY = b.MinY, MinZ = b.MinZ,
                MaxX = b.MaxX, MaxY = b.MaxY, MaxZ = b.MaxZ,
            }
            : null;

        // Reference bounds and nodes
        var referenceNodes = BuildReferenceNodes(workspace.ReferenceModels.Models);
        BoundsDto? referenceBounds = ComputeBounds(referenceNodes, useWorld: false);
        BoundsDto? combinedBounds = CombineBounds(voxelBounds, referenceBounds);

        // Materials and textures from reference models
        var materials = new List<RenderMaterial>();
        var textures = new List<RenderTexture>();
        BuildMaterialsAndTextures(workspace.ReferenceModels.Models, materials, textures);

        // Palette entries
        var paletteEntries = palette.Entries
            .Select(e => new RenderPaletteEntry
            {
                Index = e.Index,
                Name = e.Name,
                R = e.R,
                G = e.G,
                B = e.B,
                A = e.A,
                Visible = e.Index != 0,
            })
            .ToList();

        // Voxel meshes
        var voxelMeshes = new List<RenderVoxelMesh>();
        if (mesh.VertexCount > 0)
        {
            voxelMeshes.Add(new RenderVoxelMesh
            {
                Id = $"voxel-{workspace.ModelId}-r{workspace.Revision}",
                Revision = workspace.Revision,
                Positions = mesh.Positions,
                Normals = mesh.Normals,
                ColorsRgba = mesh.Colors,
                PaletteIndices = mesh.PaletteIndices ?? [],
                Indices = mesh.Indices,
                Bounds = voxelBounds,
                PayloadFormat = "json_arrays",
            });
        }

        return new RenderSceneSnapshot
        {
            SchemaVersion = "voxelforge.render_scene@1",
            Revision = workspace.Revision,
            ModelId = workspace.ModelId,
            Source = new RenderSourceInfo
            {
                Host = hostId,
                Capabilities = capabilities ?? [],
            },
            Bounds = voxelBounds,
            ReferenceBounds = referenceBounds,
            CombinedBounds = combinedBounds,
            VoxelMeshes = voxelMeshes,
            ReferenceNodes = referenceNodes,
            Materials = materials,
            Textures = textures,
            Palette = paletteEntries,
            Diagnostics = [],
        };
    }

    /// <summary>
    /// Build a lightweight render state (no mesh data) for quick state queries.
    /// </summary>
    public RenderSceneSnapshot BuildState(
        VoxelForgeWorkspaceState workspace,
        string hostId = "test")
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var referenceNodes = BuildReferenceNodeHeaders(workspace.ReferenceModels.Models);

        return new RenderSceneSnapshot
        {
            SchemaVersion = "voxelforge.render_scene@1",
            Revision = workspace.Revision,
            ModelId = workspace.ModelId,
            Source = new RenderSourceInfo
            {
                Host = hostId,
                Capabilities = [],
            },
            Bounds = null,
            ReferenceBounds = null,
            CombinedBounds = null,
            VoxelMeshes = [],
            ReferenceNodes = referenceNodes,
            Materials = [],
            Textures = [],
            Palette = workspace.Document.Model.Palette.Entries
                .Select(kvp => new RenderPaletteEntry
                {
                    Index = kvp.Key,
                    Name = kvp.Value.Name,
                    R = kvp.Value.Color.R,
                    G = kvp.Value.Color.G,
                    B = kvp.Value.Color.B,
                    A = kvp.Value.Color.A,
                    Visible = kvp.Key != 0,
                })
                .ToList(),
            Diagnostics = [],
        };
    }

    private List<RenderReferenceNode> BuildReferenceNodes(IReadOnlyList<ReferenceModelData> models)
    {
        var nodes = new List<RenderReferenceNode>(models.Count);
        for (int i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var primitives = new List<RenderPrimitive>();
            int materialIndexBase = nodes.Count; // placeholder; real material indexing happens upstream

            for (int m = 0; m < model.Meshes.Count; m++)
            {
                var mesh = model.Meshes[m];
                var verts = mesh.Vertices;

                var positions = new float[verts.Length * 3];
                var normals = new float[verts.Length * 3];
                for (int v = 0; v < verts.Length; v++)
                {
                    positions[v * 3 + 0] = verts[v].PosX;
                    positions[v * 3 + 1] = verts[v].PosY;
                    positions[v * 3 + 2] = verts[v].PosZ;
                    normals[v * 3 + 0] = verts[v].NormX;
                    normals[v * 3 + 1] = verts[v].NormY;
                    normals[v * 3 + 2] = verts[v].NormZ;
                }

                primitives.Add(new RenderPrimitive
                {
                    Id = $"{model.FilePath}-mesh{m}",
                    MaterialIndex = materialIndexBase + m,
                    Position = positions,
                    Normal = normals,
                    ColorRgba = null,
                    UvSets = BuildUvSets(mesh),
                    Indices = mesh.Indices,
                    BoundsLocal = ComputePrimitiveBounds(positions),
                });
            }

            nodes.Add(new RenderReferenceNode
            {
                Id = $"{model.FilePath}-node{i}",
                DisplayName = Path.GetFileName(model.FilePath),
                SourceFormat = model.Format,
                SourceAssetId = model.FilePath,
                Visible = model.IsVisible,
                RenderMode = "textured",
                Transform = new RenderTransform
                {
                    PositionX = model.PositionX,
                    PositionY = model.PositionY,
                    PositionZ = model.PositionZ,
                    RotationX = model.RotationX,
                    RotationY = model.RotationY,
                    RotationZ = model.RotationZ,
                    Scale = model.Scale,
                },
                BoundsLocal = ComputeNodeBounds(primitives),
                BoundsWorld = null,
                Primitives = primitives,
                Diagnostics = [],
            });
        }
        return nodes;
    }

    private List<RenderReferenceNode> BuildReferenceNodeHeaders(IReadOnlyList<ReferenceModelData> models)
    {
        return models.Select((model, i) => new RenderReferenceNode
        {
            Id = $"{model.FilePath}-node{i}",
            DisplayName = Path.GetFileName(model.FilePath),
            SourceFormat = model.Format,
            SourceAssetId = model.FilePath,
            Visible = model.IsVisible,
            RenderMode = "textured",
            Transform = new RenderTransform
            {
                PositionX = model.PositionX,
                PositionY = model.PositionY,
                PositionZ = model.PositionZ,
                RotationX = model.RotationX,
                RotationY = model.RotationY,
                RotationZ = model.RotationZ,
                Scale = model.Scale,
            },
            BoundsLocal = null,
            BoundsWorld = null,
            Primitives = [],
            Diagnostics = [],
        }).ToList();
    }

    private static List<RenderUvSet> BuildUvSets(ReferenceMeshData mesh)
    {
        var verts = mesh.Vertices;
        bool hasUvs = verts.Length > 0 && (verts[0].U != 0f || verts[0].V != 0f);
        if (!hasUvs)
            return [];

        var uvs = new float[verts.Length * 2];
        for (int v = 0; v < verts.Length; v++)
        {
            uvs[v * 2 + 0] = verts[v].U;
            uvs[v * 2 + 1] = verts[v].V;
        }

        return
        [
            new RenderUvSet
            {
                SetIndex = 0,
                Uvs = uvs,
                Origin = "top_left",
                FlipY = "asset_defined",
            }
        ];
    }

    private void BuildMaterialsAndTextures(
        IReadOnlyList<ReferenceModelData> models,
        List<RenderMaterial> materials,
        List<RenderTexture> textures)
    {
        int textureCounter = 0;

        foreach (var model in models)
        {
            foreach (var mesh in model.Meshes)
            {
                var matId = $"mat-{Guid.NewGuid():N}";
                var baseColorFactor = GetMaterialBaseColor(mesh);

                RenderTextureSlot? baseColorSlot = null;
                if (mesh.EffectiveDiffuseTexturePath is { } diffusePath && File.Exists(diffusePath))
                {
                    var texId = $"tex-{textureCounter++}";
                    textures.Add(new RenderTexture
                    {
                        Id = texId,
                        Uri = diffusePath,
                        MimeType = GetMimeType(diffusePath),
                        ColorSpace = "srgb",
                        Width = null,
                        Height = null,
                        Diagnostics = [],
                    });
                    baseColorSlot = new RenderTextureSlot
                    {
                        TextureId = texId,
                        UvSet = 0,
                        UvTransform = new RenderUvTransform
                        {
                            Offset = [0.0, 0.0],
                            Scale = [1.0, 1.0],
                            Rotation = 0.0,
                        },
                        UvOrigin = "top_left",
                        FlipY = "asset_defined",
                        WrapS = "repeat",
                        WrapT = "repeat",
                        SourceLabel = mesh.DiffuseSourceLabel ?? "assimp",
                    };
                }

                materials.Add(new RenderMaterial
                {
                    Id = matId,
                    Name = mesh.MaterialName ?? "default",
                    BaseColorFactor = baseColorFactor,
                    BaseColorTexture = baseColorSlot,
                    NormalTexture = null,
                    EmissiveTexture = null,
                    EmissiveFactor = null,
                    MetallicFactor = 0.0,
                    RoughnessFactor = 1.0,
                    AlphaMode = "opaque",
                    AlphaCutoff = null,
                    DoubleSided = false,
                    ColorSpace = "srgb",
                    Diagnostics = [],
                });
            }
        }
    }

    private static double[] GetMaterialBaseColor(ReferenceMeshData mesh)
    {
        // Try to infer from vertex colors on the first vertex
        if (mesh.Vertices.Length > 0)
        {
            var v = mesh.Vertices[0];
            if (v.R > 0 || v.G > 0 || v.B > 0)
            {
                return [v.R / 255.0, v.G / 255.0, v.B / 255.0, 1.0];
            }
        }
        return [0.8, 0.8, 0.8, 1.0]; // default medium gray
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tga" => "image/x-tga",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    private static BoundsDto? ComputeBounds(IReadOnlyList<RenderReferenceNode> nodes, bool useWorld)
    {
        bool hasAny = false;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        foreach (var node in nodes)
        {
            var bounds = node.BoundsLocal;
            if (bounds is null) continue;

            minX = Math.Min(minX, bounds.MinX);
            minY = Math.Min(minY, bounds.MinY);
            minZ = Math.Min(minZ, bounds.MinZ);
            maxX = Math.Max(maxX, bounds.MaxX);
            maxY = Math.Max(maxY, bounds.MaxY);
            maxZ = Math.Max(maxZ, bounds.MaxZ);
            hasAny = true;
        }

        return hasAny
            ? new BoundsDto { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ }
            : null;
    }

    private static BoundsDto? CombineBounds(BoundsDto? a, BoundsDto? b)
    {
        if (a is null) return b;
        if (b is null) return a;

        return new BoundsDto
        {
            MinX = Math.Min(a.MinX, b.MinX),
            MinY = Math.Min(a.MinY, b.MinY),
            MinZ = Math.Min(a.MinZ, b.MinZ),
            MaxX = Math.Max(a.MaxX, b.MaxX),
            MaxY = Math.Max(a.MaxY, b.MaxY),
            MaxZ = Math.Max(a.MaxZ, b.MaxZ),
        };
    }

    private static BoundsDto? ComputePrimitiveBounds(float[] positions)
    {
        if (positions.Length < 3) return null;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

        for (int i = 0; i < positions.Length; i += 3)
        {
            minX = Math.Min(minX, positions[i]);
            minY = Math.Min(minY, positions[i + 1]);
            minZ = Math.Min(minZ, positions[i + 2]);
            maxX = Math.Max(maxX, positions[i]);
            maxY = Math.Max(maxY, positions[i + 1]);
            maxZ = Math.Max(maxZ, positions[i + 2]);
        }

        return new BoundsDto { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ };
    }

    private static BoundsDto? ComputeNodeBounds(IReadOnlyList<RenderPrimitive> primitives)
    {
        if (primitives.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool hasAny = false;

        foreach (var prim in primitives)
        {
            if (prim.BoundsLocal is { } b)
            {
                minX = Math.Min(minX, b.MinX);
                minY = Math.Min(minY, b.MinY);
                minZ = Math.Min(minZ, b.MinZ);
                maxX = Math.Max(maxX, b.MaxX);
                maxY = Math.Max(maxY, b.MaxY);
                maxZ = Math.Max(maxZ, b.MaxZ);
                hasAny = true;
            }
        }

        return hasAny
            ? new BoundsDto { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ }
            : null;
    }
}
