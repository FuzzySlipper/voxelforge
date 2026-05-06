using System.Diagnostics;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Core;

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

        // ── Viewer API: lightweight state summary (no mesh data) ──
        routes.MapGet("/api/viewer-state", (VoxelForgeMcpSession session) =>
        {
            int revision;
            lock (session.SyncRoot)
            {
                revision = session.ViewerRevision;
            }

            var model = session.Document.Model;
            var palette = model.Palette;
            var bounds = model.GetBounds();
            int voxelCount = model.GetVoxelCount();
            string modelName = session.CurrentModelName;

            return Results.Ok(new ViewerStateResponse
            {
                Revision = revision,
                ModelName = modelName,
                VoxelCount = voxelCount,
                GridHint = model.GridHint,
                PaletteEntries = palette.Entries.Select(kvp => new ViewerPaletteEntry
                {
                    Index = kvp.Key,
                    Name = kvp.Value.Name,
                    Color = $"#{kvp.Value.Color.R:X2}{kvp.Value.Color.G:X2}{kvp.Value.Color.B:X2}",
                    A = kvp.Value.Color.A,
                    Visible = kvp.Key != 0,
                }).OrderBy(e => e.Index).ToList(),
                Bounds = bounds is { } b
                    ? new ViewerBounds { MinX = b.Min.X, MinY = b.Min.Y, MinZ = b.Min.Z, MaxX = b.Max.X, MaxY = b.Max.Y, MaxZ = b.Max.Z }
                    : null,
            });
        });

        // ── Viewer API: full mesh snapshot ──
        routes.MapGet("/api/mesh-snapshot", (VoxelForgeMcpSession session, MeshSnapshotService meshService, PaletteSnapshotService paletteService) =>
        {
            MeshSnapshot mesh;
            PaletteSnapshot palette;
            int revision;
            string modelName;

            lock (session.SyncRoot)
            {
                mesh = meshService.BuildSnapshot(session.Document.Model);
                palette = paletteService.BuildSnapshot(session.Document.Model.Palette);
                revision = session.ViewerRevision;
                modelName = session.CurrentModelName;
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
                PaletteMapping = paletteMapping.Count > 0 ? paletteMapping : null,
                Metrics = new ViewerMeshSnapshotMetrics
                {
                    MeshGenerationMs = mesh.TriangleCount > 0 ? 0 : 0,
                    SerializationMs = 0,
                    TotalMs = sw.ElapsedMilliseconds,
                },
            };
            sw.Stop();

            return Results.Ok(response);
        });

        // ── Viewer API: palette only ──
        routes.MapGet("/api/palette", (VoxelForgeMcpSession session) =>
        {
            var palette = session.Document.Model.Palette;
            return Results.Ok(new
            {
                palette_id = session.CurrentModelName,
                entries = palette.Entries.Select(kvp => new
                {
                    index = kvp.Key,
                    name = kvp.Value.Name,
                    color = $"#{kvp.Value.Color.R:X2}{kvp.Value.Color.G:X2}{kvp.Value.Color.B:X2}",
                    a = kvp.Value.Color.A,
                    visible = kvp.Key != 0,
                }).OrderBy(e => e.index).ToList(),
                entry_count = palette.Count,
            });
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
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }
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
    public Dictionary<string, object>? PaletteMapping { get; set; }
    public ViewerMeshSnapshotMetrics? Metrics { get; set; }
}

public sealed class ViewerMeshSnapshotMetrics
{
    public long MeshGenerationMs { get; set; }
    public long SerializationMs { get; set; }
    public long TotalMs { get; set; }
}
