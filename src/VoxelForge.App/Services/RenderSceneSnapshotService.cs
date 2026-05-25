using System.Numerics;
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

        // Materials and textures from reference models
        // BuildMaterialsAndTextures must run BEFORE BuildReferenceNodes so that
        // the material indices in primitives correctly index into snapshot.Materials.
        var materials = new List<RenderMaterial>();
        var textures = new List<RenderTexture>();
        BuildMaterialsAndTextures(workspace.ReferenceModels.Models, materials, textures, hostId);

        // Reference bounds and nodes (materials/textures passed for correct indexing)
        var referenceNodes = BuildReferenceNodes(workspace.ReferenceModels.Models, materials, textures, hostId);
        BoundsDto? referenceBounds = ComputeBounds(referenceNodes, useWorld: false);
        BoundsDto? referenceBoundsWorld = ComputeBounds(referenceNodes, useWorld: true);
        BoundsDto? combinedBounds = CombineBounds(voxelBounds, referenceBounds);

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
            ReferenceBounds = referenceBoundsWorld,
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

    private List<RenderReferenceNode> BuildReferenceNodes(
        IReadOnlyList<ReferenceModelData> models,
        List<RenderMaterial> materials,
        List<RenderTexture> textures,
        string hostId)
    {
        var nodes = new List<RenderReferenceNode>(models.Count);
        int cumulativeMaterialCount = 0;

        for (int i = 0; i < models.Count; i++)
        {
            var model = models[i];
            var primitives = new List<RenderPrimitive>(model.Meshes.Count);

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

                int materialIndex = cumulativeMaterialCount + m;

                primitives.Add(new RenderPrimitive
                {
                    Id = $"{model.FilePath}-mesh{m}",
                    MaterialIndex = materialIndex,
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
                BoundsWorld = ComputeWorldBounds(primitives, model),
                Primitives = primitives,
                Diagnostics = [],
            });

            cumulativeMaterialCount += model.Meshes.Count;
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
        if (verts.Length == 0)
            return [];

        // Scan ALL vertices for UV presence, not just the first vertex.
        // A mesh where every vertex has (U=0, V=0) is genuinely UV-less;
        // a mesh where only some vertices have zero UVs is not.
        bool hasUvs = verts.Any(v => v.U != 0f || v.V != 0f);
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
        List<RenderTexture> textures,
        string hostId)
    {
        int textureCounter = 0;

        for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var model = models[modelIndex];
            for (int meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                var mesh = model.Meshes[meshIndex];
                var matId = $"mat-{Guid.NewGuid():N}";
                var baseColorFactor = GetMaterialBaseColor(mesh);

                // ── Base color / diffuse texture slot ──
                RenderTextureSlot? baseColorSlot = null;
                if (mesh.EffectiveDiffuseTexturePath is { } diffusePath && File.Exists(diffusePath))
                {
                    var texId = $"tex-{textureCounter++}";
                    textures.Add(new RenderTexture
                    {
                        Id = texId,
                        // Host-safe URI: for MCP host use HTTP URL for browser loading;
                        // for other hosts (bridge, test) use transport-handle scheme.
                        Uri = hostId == "mcp"
                            ? $"/api/reference-texture?index={modelIndex}&mesh_index={meshIndex}&slot=diffuse"
                            : $"texture://{hostId}/{texId}",
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

                // ── Normal texture slot ──
                RenderTextureSlot? normalSlot = null;
                if (mesh.EffectiveNormalTexturePath is { } normalPath && File.Exists(normalPath))
                {
                    var texId = $"tex-{textureCounter++}";
                    textures.Add(new RenderTexture
                    {
                        Id = texId,
                        Uri = hostId == "mcp"
                            ? $"/api/reference-texture?index={modelIndex}&mesh_index={meshIndex}&slot=normal"
                            : $"texture://{hostId}/{texId}",
                        MimeType = GetMimeType(normalPath),
                        ColorSpace = "linear",
                        Width = null,
                        Height = null,
                        Diagnostics = [],
                    });
                    normalSlot = new RenderTextureSlot
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
                        SourceLabel = "manual_override",
                    };
                }

                // ── Emissive texture slot ──
                RenderTextureSlot? emissiveSlot = null;
                double[]? emissiveFactor = null;
                if (mesh.EffectiveEmissiveTexturePath is { } emissivePath && File.Exists(emissivePath))
                {
                    var texId = $"tex-{textureCounter++}";
                    textures.Add(new RenderTexture
                    {
                        Id = texId,
                        Uri = hostId == "mcp"
                            ? $"/api/reference-texture?index={modelIndex}&mesh_index={meshIndex}&slot=emissive"
                            : $"texture://{hostId}/{texId}",
                        MimeType = GetMimeType(emissivePath),
                        ColorSpace = "srgb",
                        Width = null,
                        Height = null,
                        Diagnostics = [],
                    });
                    emissiveSlot = new RenderTextureSlot
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
                        SourceLabel = mesh.EmissiveTextureSource is not null
                            ? "unity_sidecar"
                            : "manual_override",
                    };
                    emissiveFactor = [mesh.EmissiveBrightness, mesh.EmissiveBrightness, mesh.EmissiveBrightness];
                }

                // ── Diagnostics for missing or inferred material properties ──
                var materialDiagnostics = new List<RenderDiagnostic>();
                if (mesh.EffectiveNormalTexturePath is null)
                {
                    materialDiagnostics.Add(new RenderDiagnostic
                    {
                        Severity = "info",
                        Category = "material.normal",
                        Message = "Normal map not available for material. Flat shading used unless normal data present in mesh.",
                    });
                }
                if (mesh.EffectiveEmissiveTexturePath is null)
                {
                    materialDiagnostics.Add(new RenderDiagnostic
                    {
                        Severity = "info",
                        Category = "material.emissive",
                        Message = "Emissive texture not available for material. EmissiveFactor set to zero.",
                    });
                }

                // Alpha mode and double-sidedness: infer from vertex alpha when available,
                // otherwise emit diagnostic noting default assumptions.
                string alphaMode = "opaque";
                double? alphaCutoff = null;
                if (mesh.Vertices.Length > 0)
                {
                    bool hasAlpha = mesh.Vertices.Any(v => v.A < 255);
                    if (hasAlpha)
                    {
                        alphaMode = "blend";
                    }
                }
                bool doubleSided = false;

                materialDiagnostics.Add(new RenderDiagnostic
                {
                    Severity = "info",
                    Category = "material.alpha",
                    Message = $"Alpha mode set to \"{alphaMode}\" (inferred from vertex alpha). Use model-source metadata for authoritative value.",
                });
                if (!doubleSided)
                {
                    materialDiagnostics.Add(new RenderDiagnostic
                    {
                        Severity = "info",
                        Category = "material.double_sided",
                        Message = "DoubleSided set to false by default. Set to true if the material uses back-face culling.",
                    });
                }

                var material = new RenderMaterial
                {
                    Id = matId,
                    Name = mesh.MaterialName ?? "default",
                    BaseColorFactor = baseColorFactor,
                    BaseColorTexture = baseColorSlot,
                    NormalTexture = normalSlot,
                    EmissiveTexture = emissiveSlot,
                    EmissiveFactor = emissiveFactor,
                    MetallicFactor = 0.0,
                    RoughnessFactor = 1.0,
                    AlphaMode = alphaMode,
                    AlphaCutoff = alphaCutoff,
                    DoubleSided = doubleSided,
                    ColorSpace = "srgb",
                    Diagnostics = materialDiagnostics,
                };

                materials.Add(material);
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
            // Respect visibility: invisible nodes do not contribute to aggregate bounds.
            if (!node.Visible) continue;

            var bounds = useWorld ? node.BoundsWorld : node.BoundsLocal;
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

    /// <summary>
    /// Compute world-space bounding box for a node by transforming all 8 corners
    /// of the local primitive AABBs through the model's transform (scale, rotation, translation).
    /// </summary>
    private static BoundsDto? ComputeWorldBounds(IReadOnlyList<RenderPrimitive> primitives, ReferenceModelData model)
    {
        if (primitives.Count == 0) return null;

        // Build combined local-space bounds first
        BoundsDto? localBounds = ComputeNodeBounds(primitives);
        if (localBounds is null) return null;

        // Build transform matrix: T * R * S
        float sx = model.Scale;
        float sy = model.Scale;
        float sz = model.Scale;
        float tx = model.PositionX;
        float ty = model.PositionY;
        float tz = model.PositionZ;

        // Euler angles in degrees -> radians
        double radX = model.RotationX * Math.PI / 180.0;
        double radY = model.RotationY * Math.PI / 180.0;
        double radZ = model.RotationZ * Math.PI / 180.0;

        double cosX = Math.Cos(radX), sinX = Math.Sin(radX);
        double cosY = Math.Cos(radY), sinY = Math.Sin(radY);
        double cosZ = Math.Cos(radZ), sinZ = Math.Sin(radZ);

        // Rotation matrix R = Rz * Ry * Rx (standard Euler order for 3D scenes)
        // Rx = [[1,0,0],[0,cx,-sx],[0,sx,cx]]
        // Ry = [[cy,0,sy],[0,1,0],[-sy,0,cy]]
        // Rz = [[cz,-sz,0],[sz,cz,0],[0,0,1]]
        // Combined:
        // m00 = cy*cz, m01 = sx*sy*cz - cx*sz, m02 = cx*sy*cz + sx*sz
        // m10 = cy*sz, m11 = sx*sy*sz + cx*cz, m12 = cx*sy*sz - sx*cz
        // m20 = -sy,   m21 = sx*cy,            m22 = cx*cy
        double m00 = cosY * cosZ;
        double m01 = sinX * sinY * cosZ - cosX * sinZ;
        double m02 = cosX * sinY * cosZ + sinX * sinZ;
        double m10 = cosY * sinZ;
        double m11 = sinX * sinY * sinZ + cosX * cosZ;
        double m12 = cosX * sinY * sinZ - sinX * cosZ;
        double m20 = -sinY;
        double m21 = sinX * cosY;
        double m22 = cosX * cosY;

        // Transform all 8 corners of the local AABB
        double[] corners =
        [
            localBounds.MinX, localBounds.MinY, localBounds.MinZ,
            localBounds.MaxX, localBounds.MinY, localBounds.MinZ,
            localBounds.MinX, localBounds.MaxY, localBounds.MinZ,
            localBounds.MinX, localBounds.MinY, localBounds.MaxZ,
            localBounds.MaxX, localBounds.MaxY, localBounds.MinZ,
            localBounds.MaxX, localBounds.MinY, localBounds.MaxZ,
            localBounds.MinX, localBounds.MaxY, localBounds.MaxZ,
            localBounds.MaxX, localBounds.MaxY, localBounds.MaxZ,
        ];

        double minWx = double.MaxValue, minWy = double.MaxValue, minWz = double.MaxValue;
        double maxWx = double.MinValue, maxWy = double.MinValue, maxWz = double.MinValue;

        for (int i = 0; i < 8; i++)
        {
            double lx = corners[i * 3];
            double ly = corners[i * 3 + 1];
            double lz = corners[i * 3 + 2];

            // Scale
            double sxL = lx * sx, syL = ly * sy, szL = lz * sz;

            // Rotate (Rz * Ry * Rx)
            double rx = m00 * sxL + m01 * syL + m02 * szL;
            double ry = m10 * sxL + m11 * syL + m12 * szL;
            double rz = m20 * sxL + m21 * syL + m22 * szL;

            // Translate
            double wx = rx + tx;
            double wy = ry + ty;
            double wz = rz + tz;

            minWx = Math.Min(minWx, wx);
            minWy = Math.Min(minWy, wy);
            minWz = Math.Min(minWz, wz);
            maxWx = Math.Max(maxWx, wx);
            maxWy = Math.Max(maxWy, wy);
            maxWz = Math.Max(maxWz, wz);
        }

        return new BoundsDto { MinX = minWx, MinY = minWy, MinZ = minWz, MaxX = maxWx, MaxY = maxWy, MaxZ = maxWz };
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
