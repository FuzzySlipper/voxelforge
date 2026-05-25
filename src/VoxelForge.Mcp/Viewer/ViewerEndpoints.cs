using System.Diagnostics;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Viewer;

/// <summary>
/// Minimal API endpoints for the built-in browser viewer.
/// The viewer is a read-only WebGL output surface served from the MCP HTTP host.
/// </summary>
public static class ViewerEndpoints
{
    /// <summary>
    /// Map viewer and viewer-API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapViewerEndpoints(this IEndpointRouteBuilder routes)
    {
        // ── Viewer HTML page ──
        routes.MapGet("/viewer", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(ViewerHtml.Content);
        });

        // ── Viewer API: lightweight state summary (no mesh data, thread-safe) ──
        routes.MapGet("/api/viewer-state", (VoxelForgeMcpSession session) =>
        {
            int revision;
            string modelName;
            int voxelCount;
            int gridHint;
            int referenceModelCount;
            int referenceVertexCount;
            List<ViewerPaletteEntry> paletteEntries;
            ViewerBounds? bounds;

            lock (session.SyncRoot)
            {
                revision = session.ViewerRevision;
                modelName = session.CurrentModelName;
                var model = session.Document.Model;
                voxelCount = model.GetVoxelCount();
                gridHint = model.GridHint;

                var palette = model.Palette;
                paletteEntries = palette.Entries.Select(kvp => new ViewerPaletteEntry
                {
                    Index = kvp.Key,
                    Name = kvp.Value.Name,
                    Color = $"#{kvp.Value.Color.R:X2}{kvp.Value.Color.G:X2}{kvp.Value.Color.B:X2}",
                    A = kvp.Value.Color.A,
                    Visible = kvp.Key != 0,
                }).OrderBy(e => e.Index).ToList();

                referenceModelCount = session.ReferenceModels.Models.Count;
                referenceVertexCount = session.ReferenceModels.Models.Sum(r => r.TotalVertices);

                // Compute combined bounds: voxel model bounds + visible reference model bounds.
                var modelBounds = model.GetBounds();
                bounds = ComputeCombinedBounds(modelBounds, session.ReferenceModels.Models);
            }

            return Results.Ok(new ViewerStateResponse
            {
                Revision = revision,
                ModelName = modelName,
                VoxelCount = voxelCount,
                GridHint = gridHint,
                PaletteEntries = paletteEntries,
                Bounds = bounds,
                ReferenceModelCount = referenceModelCount,
                ReferenceVertexCount = referenceVertexCount,
            });
        });

        // ── Viewer API: full mesh snapshot (thread-safe) ──
        routes.MapGet("/api/mesh-snapshot", (VoxelForgeMcpSession session, MeshSnapshotService meshService, PaletteSnapshotService paletteService) =>
        {
            MeshSnapshot mesh;
            PaletteSnapshot palette;
            int revision;
            string modelName;
            long meshGenerationMs;
            List<ViewerReferenceModelData> referenceModels;

            lock (session.SyncRoot)
            {
                var meshStopwatch = Stopwatch.StartNew();
                mesh = meshService.BuildSnapshot(session.Document.Model);
                meshStopwatch.Stop();
                meshGenerationMs = meshStopwatch.ElapsedMilliseconds;
                palette = paletteService.BuildSnapshot(session.Document.Model.Palette);
                revision = session.ViewerRevision;
                modelName = session.CurrentModelName;
                referenceModels = BuildReferenceModelDataList(session.ReferenceModels.Models);
            }

            string meshId = $"mesh-{modelName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-r{revision}";

            var paletteMapping = new Dictionary<string, object>();
            foreach (var entry in palette.Entries)
            {
                paletteMapping[entry.Index.ToString()] = new
                {
                    name = entry.Name,
                    color = $"#{entry.R:X2}{entry.G:X2}{entry.B:X2}",
                    a = entry.A,
                    visible = true,
                };
            }

            var sw = Stopwatch.StartNew();
            var response = new ViewerMeshSnapshotResponse
            {
                ModelId = modelName,
                MeshId = meshId,
                Format = "json",
                VertexCount = mesh.VertexCount,
                IndexCount = mesh.Indices.Length,
                TriangleCount = mesh.TriangleCount,
                Positions = mesh.Positions,
                Normals = mesh.Normals,
                Colors = ExpandBytes(mesh.Colors),
                Indices = mesh.Indices,
                Bounds = mesh.Bounds is { } b
                    ? new ViewerBounds { MinX = b.MinX, MinY = b.MinY, MinZ = b.MinZ, MaxX = b.MaxX, MaxY = b.MaxY, MaxZ = b.MaxZ }
                    : null,
                // Combined bounds include visible reference model geometry, for
                // camera framing when the voxel model is empty but refs exist.
                CombinedBounds = ComputeCombinedBounds(
                    mesh.Bounds is { } mb
                        ? (new Point3(mb.MinX, mb.MinY, mb.MinZ), new Point3(mb.MaxX, mb.MaxY, mb.MaxZ))
                        : null,
                    session.ReferenceModels.Models),
                PaletteMapping = paletteMapping.Count > 0 ? paletteMapping : null,
                Metrics = new ViewerMeshSnapshotMetrics
                {
                    MeshGenerationMs = meshGenerationMs,
                    SerializationMs = 0,
                    TotalMs = sw.ElapsedMilliseconds,
                },
                ReferenceModels = referenceModels,
            };
            sw.Stop();

            return Results.Ok(response);
        });

        // ── Viewer API: palette only (thread-safe) ──
        routes.MapGet("/api/palette", (VoxelForgeMcpSession session) =>
        {
            string modelName;
            int entryCount;
            List<ViewerPaletteEntry> entries;

            lock (session.SyncRoot)
            {
                modelName = session.CurrentModelName;
                var palette = session.Document.Model.Palette;
                entries = palette.Entries.Select(kvp => new ViewerPaletteEntry
                {
                    Index = kvp.Key,
                    Name = kvp.Value.Name,
                    Color = $"#{kvp.Value.Color.R:X2}{kvp.Value.Color.G:X2}{kvp.Value.Color.B:X2}",
                    A = kvp.Value.Color.A,
                    Visible = kvp.Key != 0,
                }).OrderBy(e => e.Index).ToList();
                entryCount = palette.Count;
            }

            return Results.Ok(new
            {
                palette_id = modelName,
                entries,
                entry_count = entryCount,
            });
        });

        // ── Viewer API: Server-Sent Events for live revision updates ──
        routes.MapGet("/api/viewer-events", async (HttpContext context, VoxelForgeMcpSession session) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var cancellationToken = context.RequestAborted;
            var (reader, unsubscribe) = session.SubscribeViewerEvents();

            try
            {
                // Send initial keepalive with current revision.
                int currentRevision = session.ViewerRevision;
                await context.Response.WriteAsync(
                    $"data: {{\"type\":\"connected\",\"revision\":{currentRevision}}}\n\n",
                    cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);

                // Stream revision events as they arrive.
                await foreach (var revision in reader.ReadAllAsync(cancellationToken))
                {
                    await context.Response.WriteAsync(
                        $"data: {{\"type\":\"revision\",\"revision\":{revision}}}\n\n",
                        cancellationToken);
                    await context.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — normal cleanup.
            }
            finally
            {
                unsubscribe();
            }
        });

        // ── Viewer API: serve reference model texture files ──
        // Only serves textures that are referenced by loaded reference models.
        // Validates model index, mesh index, and slot against the session state
        // to prevent arbitrary filesystem access.
        routes.MapGet("/api/reference-texture", async (HttpContext context, VoxelForgeMcpSession session) =>
        {
            if (!int.TryParse(context.Request.Query["index"], out int modelIndex))
                return Results.BadRequest("Missing or invalid 'index' query parameter.");

            if (!int.TryParse(context.Request.Query["mesh_index"], out int meshIndex))
                return Results.BadRequest("Missing or invalid 'mesh_index' query parameter.");

            var slot = context.Request.Query["slot"].FirstOrDefault() ?? "diffuse";

            lock (session.SyncRoot)
            {
                var model = session.ReferenceModels.Get(modelIndex);
                if (model is null)
                    return Results.NotFound($"No reference model at index {modelIndex}.");

                if (meshIndex < 0 || meshIndex >= model.Meshes.Count)
                    return Results.NotFound($"Mesh index {meshIndex} out of range.");

                var mesh = model.Meshes[meshIndex];

                string? texturePath = slot.ToLowerInvariant() switch
                {
                    "diffuse" => mesh.EffectiveDiffuseTexturePath,
                    "normal" => mesh.EffectiveNormalTexturePath,
                    "emissive" => mesh.EffectiveEmissiveTexturePath,
                    _ => null,
                };

                if (texturePath is null)
                    return Results.NotFound($"No texture for slot '{slot}' on mesh [{modelIndex}][{meshIndex}].");

                if (!File.Exists(texturePath))
                    return Results.NotFound($"Texture file no longer exists on disk: {texturePath}");

                // Determine content type
                var ext = Path.GetExtension(texturePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".bmp" => "image/bmp",
                    ".tga" => "image/x-tga",
                    ".webp" => "image/webp",
                    _ => "application/octet-stream",
                };

                return Results.File(texturePath, contentType);
            }
        });

        return routes;
    }

    private static int[] ExpandBytes(byte[] values)
    {
        var expanded = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
            expanded[i] = values[i];
        return expanded;
    }

    /// <summary>
    /// Build a list of <see cref="ViewerReferenceModelData"/> from the session's reference models,
    /// extracting vertex geometry and transform data for the browser viewer to render.
    /// </summary>
    internal static List<ViewerReferenceModelData> BuildReferenceModelDataList(IReadOnlyList<ReferenceModelData> models)
    {
        var result = new List<ViewerReferenceModelData>(models.Count);
        for (int i = 0; i < models.Count; i++)
        {
            var refModel = models[i];
            var allPositions = new List<float>();
            var allNormals = new List<float>();
            var allColors = new List<int>();
            var allUvs = new List<float>();
            var allIndices = new List<int>();
            int indexOffset = 0;
            var perMeshGeometries = new List<ViewerReferenceMeshGeometry>(refModel.Meshes.Count);

            for (int m = 0; m < refModel.Meshes.Count; m++)
            {
                var mesh = refModel.Meshes[m];
                var verts = mesh.Vertices;

                var meshPositions = new List<float>(verts.Length * 3);
                var meshNormals = new List<float>(verts.Length * 3);
                var meshColors = new List<int>(verts.Length * 4);
                var meshUvs = new List<float>(verts.Length * 2);
                bool meshHasUvs = false;

                for (int v = 0; v < verts.Length; v++)
                {
                    var vert = verts[v];
                    meshPositions.Add(vert.PosX);
                    meshPositions.Add(vert.PosY);
                    meshPositions.Add(vert.PosZ);
                    meshNormals.Add(vert.NormX);
                    meshNormals.Add(vert.NormY);
                    meshNormals.Add(vert.NormZ);
                    meshUvs.Add(vert.U);
                    meshUvs.Add(vert.V);
                    if (vert.U != 0f || vert.V != 0f) meshHasUvs = true;
                    // Use vertex color if available, otherwise medium gray fallback.
                    if (vert.R > 0 || vert.G > 0 || vert.B > 0 || vert.A > 0)
                    {
                        meshColors.Add(vert.R);
                        meshColors.Add(vert.G);
                        meshColors.Add(vert.B);
                        meshColors.Add(vert.A);
                    }
                    else
                    {
                        meshColors.Add(128);
                        meshColors.Add(128);
                        meshColors.Add(128);
                        meshColors.Add(255);
                    }
                }

                // Build per-mesh geometry entry
                perMeshGeometries.Add(new ViewerReferenceMeshGeometry
                {
                    MeshIndex = m,
                    MaterialName = mesh.MaterialName,
                    VertexCount = verts.Length,
                    TriangleCount = mesh.Indices.Length / 3,
                    Positions = [..meshPositions],
                    Normals = [..meshNormals],
                    Colors = [..meshColors],
                    Uvs = [..meshUvs],
                    HasUvs = meshHasUvs,
                    Indices = [..mesh.Indices],
                    DiffuseTexturePath = mesh.EffectiveDiffuseTexturePath,
                    NormalTexturePath = mesh.EffectiveNormalTexturePath,
                    EmissiveTexturePath = mesh.EffectiveEmissiveTexturePath,
                    DiffuseSourceLabel = mesh.DiffuseSourceLabel,
                });

                // Also add to flattened model-level arrays (backward compat)
                allPositions.AddRange(meshPositions);
                allNormals.AddRange(meshNormals);
                allColors.AddRange(meshColors);
                allUvs.AddRange(meshUvs);
                for (int idx = 0; idx < mesh.Indices.Length; idx++)
                {
                    allIndices.Add(mesh.Indices[idx] + indexOffset);
                }

                indexOffset += verts.Length;
            }

            // Compute local bounds from all positions.
            ViewerBounds? bounds = null;
            if (allPositions.Count >= 3)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                for (int p = 0; p < allPositions.Count; p += 3)
                {
                    minX = Math.Min(minX, allPositions[p]);
                    minY = Math.Min(minY, allPositions[p + 1]);
                    minZ = Math.Min(minZ, allPositions[p + 2]);
                    maxX = Math.Max(maxX, allPositions[p]);
                    maxY = Math.Max(maxY, allPositions[p + 1]);
                    maxZ = Math.Max(maxZ, allPositions[p + 2]);
                }
                bounds = new ViewerBounds { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ };
            }

            result.Add(new ViewerReferenceModelData
            {
                Index = i,
                FileName = Path.GetFileName(refModel.FilePath),
                Format = refModel.Format,
                TotalVertices = allPositions.Count / 3,
                TotalTriangles = allIndices.Count / 3,
                IsVisible = refModel.IsVisible,
                PositionX = refModel.PositionX,
                PositionY = refModel.PositionY,
                PositionZ = refModel.PositionZ,
                RotationX = refModel.RotationX,
                RotationY = refModel.RotationY,
                RotationZ = refModel.RotationZ,
                Scale = refModel.Scale,
                Positions = [..allPositions],
                Normals = [..allNormals],
                Colors = [..allColors],
                Uvs = [..allUvs],
                Indices = [..allIndices],
                Bounds = bounds,
                MeshTextures = refModel.Meshes.Select((mesh, mi) => new ViewerMeshTextureInfo
                {
                    MeshIndex = mi,
                    MaterialName = mesh.MaterialName,
                    DiffuseTexturePath = mesh.EffectiveDiffuseTexturePath,
                    NormalTexturePath = mesh.EffectiveNormalTexturePath,
                    EmissiveTexturePath = mesh.EffectiveEmissiveTexturePath,
                    DiffuseSourceLabel = mesh.DiffuseSourceLabel,
                }).ToList(),
                // Per-mesh geometry for fine-grained viewer rendering
                Meshes = perMeshGeometries,
            });
        }

        return result;
    }

    /// <summary>
    /// Compute combined axis-aligned bounding box from voxel model bounds and visible
    /// reference model applied-transforms. Returns null when both are empty.
    /// </summary>
    internal static ViewerBounds? ComputeCombinedBounds(
        (Point3 Min, Point3 Max)? voxelBounds,
        IReadOnlyList<ReferenceModelData> referenceModels)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool hasAny = false;

        if (voxelBounds is { } vb)
        {
            minX = Math.Min(minX, vb.Min.X);
            minY = Math.Min(minY, vb.Min.Y);
            minZ = Math.Min(minZ, vb.Min.Z);
            maxX = Math.Max(maxX, vb.Max.X);
            maxY = Math.Max(maxY, vb.Max.Y);
            maxZ = Math.Max(maxZ, vb.Max.Z);
            hasAny = true;
        }

        foreach (var refModel in referenceModels)
        {
            if (!refModel.IsVisible) continue;

            foreach (var mesh in refModel.Meshes)
            {
                foreach (var vert in mesh.Vertices)
                {
                    // Apply transform: scale -> rotate -> translate
                    var (tx, ty, tz) = ApplyReferenceTransform(
                        vert.PosX, vert.PosY, vert.PosZ,
                        refModel.PositionX, refModel.PositionY, refModel.PositionZ,
                        refModel.RotationX, refModel.RotationY, refModel.RotationZ,
                        refModel.Scale);

                    minX = Math.Min(minX, tx);
                    minY = Math.Min(minY, ty);
                    minZ = Math.Min(minZ, tz);
                    maxX = Math.Max(maxX, tx);
                    maxY = Math.Max(maxY, ty);
                    maxZ = Math.Max(maxZ, tz);
                    hasAny = true;
                }
            }
        }

        return hasAny
            ? new ViewerBounds { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ }
            : null;
    }

    /// <summary>
    /// Apply reference model transform (scale -> rotate ZYX -> translate) to a local position.
    /// Rotation angles are in degrees.
    /// </summary>
    private static (double X, double Y, double Z) ApplyReferenceTransform(
        float localX, float localY, float localZ,
        float posX, float posY, float posZ,
        float rotXDeg, float rotYDeg, float rotZDeg,
        float scale)
    {
        double x = localX * scale;
        double y = localY * scale;
        double z = localZ * scale;

        // Rotation Z (yaw) in degrees
        double rz = rotZDeg * Math.PI / 180.0;
        double cosZ = Math.Cos(rz), sinZ = Math.Sin(rz);
        double rx = x * cosZ - y * sinZ;
        double ry = x * sinZ + y * cosZ;
        x = rx; y = ry;

        // Rotation Y (pitch) in degrees
        double ryDeg = rotYDeg * Math.PI / 180.0;
        double cosY = Math.Cos(ryDeg), sinY = Math.Sin(ryDeg);
        double rzTmp = z * cosY - x * sinY;
        x = z * sinY + x * cosY;
        z = rzTmp;

        // Rotation X (roll) in degrees
        double rxDeg = rotXDeg * Math.PI / 180.0;
        double cosX = Math.Cos(rxDeg), sinX = Math.Sin(rxDeg);
        double ryTmp = y * cosX - z * sinX;
        z = y * sinX + z * cosX;
        y = ryTmp;

        return (x + posX, y + posY, z + posZ);
    }
}

// ── View Models (DTOs matching TS interfaces) ──

public sealed class ViewerStateResponse
{
    public int Revision { get; set; }
    public string ModelName { get; set; } = "";
    public int VoxelCount { get; set; }
    public int GridHint { get; set; }
    public List<ViewerPaletteEntry> PaletteEntries { get; set; } = [];
    public ViewerBounds? Bounds { get; set; }
    public int ReferenceModelCount { get; set; }
    public int ReferenceVertexCount { get; set; }
}

public sealed class ViewerPaletteEntry
{
    public byte Index { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#000000";
    public byte A { get; set; } = 255;
    public bool Visible { get; set; } = true;
}

public sealed class ViewerBounds
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MinZ { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double MaxZ { get; set; }
}

public sealed class ViewerMeshSnapshotResponse
{
    public string ModelId { get; set; } = "";
    public string MeshId { get; set; } = "";
    public string Format { get; set; } = "json";
    public int VertexCount { get; set; }
    public int IndexCount { get; set; }
    public int TriangleCount { get; set; }
    public float[] Positions { get; set; } = [];
    public float[] Normals { get; set; } = [];
    /// <summary>
    /// Flat RGBA color bytes represented as JSON numbers. Do not expose this
    /// as byte[] because System.Text.Json serializes byte[] as base64 strings,
    /// which browser renderer code cannot treat as vertex color arrays.
    /// </summary>
    public int[] Colors { get; set; } = [];
    public int[] Indices { get; set; } = [];
    public ViewerBounds? Bounds { get; set; }
    /// <summary>
    /// Combined bounds including visible reference model geometry, for camera
    /// framing when the voxel model is empty but reference models exist.
    /// Null when no geometry exists at all.
    /// </summary>
    public ViewerBounds? CombinedBounds { get; set; }
    public Dictionary<string, object>? PaletteMapping { get; set; }
    public ViewerMeshSnapshotMetrics? Metrics { get; set; }
    public List<ViewerReferenceModelData>? ReferenceModels { get; set; }
}

public sealed class ViewerMeshSnapshotMetrics
{
    public long MeshGenerationMs { get; set; }
    public long SerializationMs { get; set; }
    public long TotalMs { get; set; }
}

/// <summary>
/// Full geometry + transform data for a reference model, included in /api/mesh-snapshot.
/// </summary>
public sealed class ViewerReferenceModelData
{
    public int Index { get; set; }
    public string FileName { get; set; } = "";
    public string Format { get; set; } = "";
    public int TotalVertices { get; set; }
    public int TotalTriangles { get; set; }
    public bool IsVisible { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float Scale { get; set; } = 1f;
    /// <summary>Flat interleaved positions (x,y,z triplets). In local (untransformed) coordinates.</summary>
    public float[] Positions { get; set; } = [];
    /// <summary>Flat interleaved normals (x,y,z triplets). In local coordinates.</summary>
    public float[] Normals { get; set; } = [];
    /// <summary>Flat RGBA color bytes as int array. Fallback to medium gray (128,128,128,255) when absent.</summary>
    public int[] Colors { get; set; } = [];
    /// <summary>Flat interleaved UV coordinates (u,v pairs) when the source mesh has texture coordinates.</summary>
    public float[] Uvs { get; set; } = [];
    /// <summary>Triangle index data.</summary>
    public int[] Indices { get; set; } = [];
    /// <summary>Bounds of the local (untransformed) reference geometry.</summary>
    public ViewerBounds? Bounds { get; set; }
    /// <summary>
    /// Per-mesh texture info for the viewer to load and apply. One entry per mesh.
    /// Each entry contains effective texture paths and source labels for each slot.
    /// </summary>
    public List<ViewerMeshTextureInfo>? MeshTextures { get; set; }
    /// <summary>
    /// Per-mesh geometry data. One entry per source mesh, enabling the viewer
    /// to build individual Three.js Mesh objects with per-mesh materials and
    /// textures instead of applying the first mesh's texture globally.
    /// </summary>
    public List<ViewerReferenceMeshGeometry>? Meshes { get; set; }
}

/// <summary>
/// Per-mesh geometry data for a reference model mesh. Carried alongside the
/// flattened model-level geometry so the browser viewer can build individual
/// Three.js Mesh objects with per-mesh materials and textures.
/// </summary>
public sealed class ViewerReferenceMeshGeometry
{
    public int MeshIndex { get; set; }
    public string MaterialName { get; set; } = "";
    public int VertexCount { get; set; }
    public int TriangleCount { get; set; }
    /// <summary>Flat interleaved positions (x,y,z triplets). In local (untransformed) coordinates.</summary>
    public float[] Positions { get; set; } = [];
    /// <summary>Flat interleaved normals (x,y,z triplets). In local coordinates.</summary>
    public float[] Normals { get; set; } = [];
    /// <summary>Flat RGBA color bytes as int array. Fallback to medium gray (128,128,128,255) when absent.</summary>
    public int[] Colors { get; set; } = [];
    /// <summary>Flat interleaved UV coordinates (u,v pairs) when the source mesh has texture coordinates.</summary>
    public float[] Uvs { get; set; } = [];
    /// <summary>Whether this mesh has non-zero UV coordinates from the source asset.</summary>
    public bool HasUvs { get; set; }
    /// <summary>Triangle index data.</summary>
    public int[] Indices { get; set; } = [];
    /// <summary>Effective diffuse texture path (manual override wins, then import path). Null if none.</summary>
    public string? DiffuseTexturePath { get; set; }
    /// <summary>Effective normal texture path (always manual override). Null if none.</summary>
    public string? NormalTexturePath { get; set; }
    /// <summary>Effective emissive texture path (manual override wins, then import path). Null if none.</summary>
    public string? EmissiveTexturePath { get; set; }
    /// <summary>Human-readable label for the diffuse texture source: "manual_override", "assimp", "unity_sidecar", or "none".</summary>
    public string DiffuseSourceLabel { get; set; } = "none";
}

/// <summary>
/// Per-mesh texture information exposed in the mesh-snapshot response.
/// Enables the browser viewer to load and apply textures via THREE.TextureLoader.
/// </summary>
public sealed class ViewerMeshTextureInfo
{
    public int MeshIndex { get; set; }
    public string MaterialName { get; set; } = "";
    public string? DiffuseTexturePath { get; set; }
    public string? NormalTexturePath { get; set; }
    public string? EmissiveTexturePath { get; set; }
    public string DiffuseSourceLabel { get; set; } = "none";
}
