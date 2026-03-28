using System.Numerics;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.Voxelization;

/// <summary>
/// Converts a triangle mesh into a VoxelModel by ray-based sampling.
/// Supports surface (shell only) and solid (filled interior) modes.
/// When vertex colors are provided, interpolates them at hit points and
/// builds a palette from the resulting voxel colors.
/// </summary>
public sealed class VoxelizeService
{
    private readonly ILogger<VoxelizeService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public VoxelizeService(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<VoxelizeService>();
    }

    public VoxelModel Voxelize(TriangleMesh mesh, int resolution, VoxelizeMode mode, byte paletteIndex = 1)
    {
        // Compute AABB
        var min = mesh.Positions[0];
        var max = mesh.Positions[0];
        for (int i = 1; i < mesh.Positions.Length; i++)
        {
            min = Vector3.Min(min, mesh.Positions[i]);
            max = Vector3.Max(max, mesh.Positions[i]);
        }

        var size = max - min;
        float maxDim = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        float cellSize = maxDim / resolution;

        // Add small padding to avoid edge cases
        min -= new Vector3(cellSize * 0.5f);

        _logger.LogInformation("Voxelizing: {Triangles} triangles, resolution {Res}, mode {Mode}",
            mesh.TriangleCount, resolution, mode);

        bool hasColors = mesh.VertexColors is not null;

        var voxels = new bool[resolution, resolution, resolution];

        // Color accumulation arrays (only allocated when vertex colors exist)
        int[,,]? accumR = null, accumG = null, accumB = null, accumCount = null;
        if (hasColors)
        {
            accumR = new int[resolution, resolution, resolution];
            accumG = new int[resolution, resolution, resolution];
            accumB = new int[resolution, resolution, resolution];
            accumCount = new int[resolution, resolution, resolution];
        }

        // Cast rays along each axis
        for (int axis = 0; axis < 3; axis++)
        {
            int uAxis = (axis + 1) % 3;
            int vAxis = (axis + 2) % 3;

            Parallel.For(0, resolution, uIdx =>
            {
                var hits = hasColors
                    ? new List<(float t, int triBase)>()
                    : null;
                var hitsSimple = hasColors ? null : new List<float>();

                for (int vIdx = 0; vIdx < resolution; vIdx++)
                {
                    hits?.Clear();
                    hitsSimple?.Clear();

                    // Build ray: origin at (u, v) cell center, direction along axis
                    var origin = new float[3];
                    origin[uAxis] = min[uAxis] + (uIdx + 0.5f) * cellSize;
                    origin[vAxis] = min[vAxis] + (vIdx + 0.5f) * cellSize;
                    origin[axis] = min[axis] - cellSize;

                    var rayOrigin = new Vector3(origin[0], origin[1], origin[2]);
                    var rayDir = new Vector3(
                        axis == 0 ? 1f : 0f,
                        axis == 1 ? 1f : 0f,
                        axis == 2 ? 1f : 0f);

                    // Test against all triangles
                    for (int triIdx = 0; triIdx < mesh.Indices.Length; triIdx += 3)
                    {
                        var p0 = mesh.Positions[mesh.Indices[triIdx]];
                        var p1 = mesh.Positions[mesh.Indices[triIdx + 1]];
                        var p2 = mesh.Positions[mesh.Indices[triIdx + 2]];

                        if (RayTriangleIntersect(rayOrigin, rayDir, p0, p1, p2, out float tHit))
                        {
                            if (hasColors)
                                hits!.Add((tHit, triIdx));
                            else
                                hitsSimple!.Add(tHit);
                        }
                    }

                    if (hasColors)
                    {
                        if (hits!.Count == 0) continue;
                        hits.Sort((a, b) => a.t.CompareTo(b.t));
                        ProcessHitsWithColor(hits, mesh, axis, uIdx, vIdx, min, cellSize,
                            resolution, mode, voxels, rayOrigin, rayDir,
                            accumR!, accumG!, accumB!, accumCount!);
                    }
                    else
                    {
                        if (hitsSimple!.Count == 0) continue;
                        hitsSimple.Sort();
                        ProcessHitsSimple(hitsSimple, axis, uIdx, vIdx, min, cellSize,
                            resolution, mode, voxels);
                    }
                }
            });
        }

        // Build VoxelModel from results
        var model = new VoxelModel(_loggerFactory.CreateLogger<VoxelModel>()) { GridHint = resolution };

        if (hasColors)
            BuildModelWithColors(model, voxels, accumR!, accumG!, accumB!, accumCount!, resolution);
        else
            BuildModelSimple(model, voxels, resolution, paletteIndex);

        _logger.LogInformation("Voxelization complete: {Count} voxels", model.GetVoxelCount());
        return model;
    }

    private static void ProcessHitsSimple(List<float> hits, int axis, int uIdx, int vIdx,
        Vector3 min, float cellSize, int resolution, VoxelizeMode mode, bool[,,] voxels)
    {
        if (mode == VoxelizeMode.Surface)
        {
            foreach (float tHit in hits)
            {
                float worldPos = min[axis] - cellSize + tHit;
                int w = (int)((worldPos - min[axis]) / cellSize);
                if (w >= 0 && w < resolution)
                    voxels[GetX(axis, uIdx, vIdx, w), GetY(axis, uIdx, vIdx, w), GetZ(axis, uIdx, vIdx, w)] = true;
            }
        }
        else // Solid
        {
            for (int i = 0; i + 1 < hits.Count; i += 2)
            {
                float startPos = min[axis] - cellSize + hits[i];
                float endPos = min[axis] - cellSize + hits[i + 1];
                int wStart = (int)((startPos - min[axis]) / cellSize);
                int wEnd = (int)((endPos - min[axis]) / cellSize);

                for (int w = Math.Max(0, wStart); w <= Math.Min(resolution - 1, wEnd); w++)
                    voxels[GetX(axis, uIdx, vIdx, w), GetY(axis, uIdx, vIdx, w), GetZ(axis, uIdx, vIdx, w)] = true;
            }
        }
    }

    private static void ProcessHitsWithColor(List<(float t, int triBase)> hits,
        TriangleMesh mesh, int axis, int uIdx, int vIdx,
        Vector3 min, float cellSize, int resolution, VoxelizeMode mode,
        bool[,,] voxels, Vector3 rayOrigin, Vector3 rayDir,
        int[,,] accumR, int[,,] accumG, int[,,] accumB, int[,,] accumCount)
    {
        var colors = mesh.VertexColors!;

        if (mode == VoxelizeMode.Surface)
        {
            foreach (var (tHit, triBase) in hits)
            {
                float worldPos = min[axis] - cellSize + tHit;
                int w = (int)((worldPos - min[axis]) / cellSize);
                if (w < 0 || w >= resolution) continue;

                int x = GetX(axis, uIdx, vIdx, w);
                int y = GetY(axis, uIdx, vIdx, w);
                int z = GetZ(axis, uIdx, vIdx, w);
                voxels[x, y, z] = true;

                var color = InterpolateTriangleColor(mesh, triBase, rayOrigin, rayDir);
                Interlocked.Add(ref accumR[x, y, z], color.R);
                Interlocked.Add(ref accumG[x, y, z], color.G);
                Interlocked.Add(ref accumB[x, y, z], color.B);
                Interlocked.Add(ref accumCount[x, y, z], 1);
            }
        }
        else // Solid
        {
            // For solid mode, use entry/exit pairs. Interior voxels get the
            // color of the nearest surface hit.
            for (int i = 0; i + 1 < hits.Count; i += 2)
            {
                var entry = hits[i];
                var exit = hits[i + 1];

                float startPos = min[axis] - cellSize + entry.t;
                float endPos = min[axis] - cellSize + exit.t;
                int wStart = (int)((startPos - min[axis]) / cellSize);
                int wEnd = (int)((endPos - min[axis]) / cellSize);

                var entryColor = InterpolateTriangleColor(mesh, entry.triBase, rayOrigin, rayDir);
                var exitColor = InterpolateTriangleColor(mesh, exit.triBase, rayOrigin, rayDir);

                int wMin = Math.Max(0, wStart);
                int wMax = Math.Min(resolution - 1, wEnd);
                int span = wMax - wMin;

                for (int w = wMin; w <= wMax; w++)
                {
                    int x = GetX(axis, uIdx, vIdx, w);
                    int y = GetY(axis, uIdx, vIdx, w);
                    int z = GetZ(axis, uIdx, vIdx, w);
                    voxels[x, y, z] = true;

                    // Lerp between entry and exit colors
                    float blend = span > 0 ? (float)(w - wMin) / span : 0.5f;
                    byte r = (byte)(entryColor.R + (exitColor.R - entryColor.R) * blend);
                    byte g = (byte)(entryColor.G + (exitColor.G - entryColor.G) * blend);
                    byte b = (byte)(entryColor.B + (exitColor.B - entryColor.B) * blend);

                    Interlocked.Add(ref accumR[x, y, z], r);
                    Interlocked.Add(ref accumG[x, y, z], g);
                    Interlocked.Add(ref accumB[x, y, z], b);
                    Interlocked.Add(ref accumCount[x, y, z], 1);
                }
            }
        }
    }

    private static RgbaColor InterpolateTriangleColor(TriangleMesh mesh, int triBase,
        Vector3 rayOrigin, Vector3 rayDir)
    {
        var colors = mesh.VertexColors!;
        var p0 = mesh.Positions[mesh.Indices[triBase]];
        var p1 = mesh.Positions[mesh.Indices[triBase + 1]];
        var p2 = mesh.Positions[mesh.Indices[triBase + 2]];

        RayTriangleIntersectBary(rayOrigin, rayDir, p0, p1, p2, out _, out float baryU, out float baryV);
        float baryW = 1f - baryU - baryV;

        var c0 = colors[mesh.Indices[triBase]];
        var c1 = colors[mesh.Indices[triBase + 1]];
        var c2 = colors[mesh.Indices[triBase + 2]];

        return new RgbaColor(
            (byte)Math.Clamp((int)(c0.R * baryW + c1.R * baryU + c2.R * baryV), 0, 255),
            (byte)Math.Clamp((int)(c0.G * baryW + c1.G * baryU + c2.G * baryV), 0, 255),
            (byte)Math.Clamp((int)(c0.B * baryW + c1.B * baryU + c2.B * baryV), 0, 255));
    }

    private void BuildModelSimple(VoxelModel model, bool[,,] voxels, int resolution, byte paletteIndex)
    {
        for (int x = 0; x < resolution; x++)
        for (int y = 0; y < resolution; y++)
        for (int z = 0; z < resolution; z++)
        {
            if (voxels[x, y, z])
                model.SetVoxel(new Point3(x, y, z), paletteIndex);
        }
    }

    private void BuildModelWithColors(VoxelModel model, bool[,,] voxels,
        int[,,] accumR, int[,,] accumG, int[,,] accumB, int[,,] accumCount, int resolution)
    {
        // First pass: collect average colors for all voxels
        var voxelColors = new List<(Point3 pos, RgbaColor color)>();
        for (int x = 0; x < resolution; x++)
        for (int y = 0; y < resolution; y++)
        for (int z = 0; z < resolution; z++)
        {
            if (!voxels[x, y, z]) continue;
            int count = accumCount[x, y, z];
            RgbaColor color;
            if (count > 0)
            {
                color = new RgbaColor(
                    (byte)Math.Clamp(accumR[x, y, z] / count, 0, 255),
                    (byte)Math.Clamp(accumG[x, y, z] / count, 0, 255),
                    (byte)Math.Clamp(accumB[x, y, z] / count, 0, 255));
            }
            else
            {
                // Voxel was filled (solid interior) but no color data — use grey
                color = new RgbaColor(128, 128, 128);
            }
            voxelColors.Add((new Point3(x, y, z), color));
        }

        // Build palette via greedy nearest-neighbor quantization
        var palette = model.Palette;
        // Map from quantized color key to palette index
        var colorToPalette = new Dictionary<(byte r, byte g, byte b), byte>();
        byte nextIndex = 1;
        const int quantShift = 2; // Reduce to 6-bit per channel for initial grouping

        foreach (var (pos, color) in voxelColors)
        {
            // Quantize color to reduce unique count
            byte qr = (byte)((color.R >> quantShift) << quantShift);
            byte qg = (byte)((color.G >> quantShift) << quantShift);
            byte qb = (byte)((color.B >> quantShift) << quantShift);
            var key = (qr, qg, qb);

            if (!colorToPalette.TryGetValue(key, out byte palIdx))
            {
                if (nextIndex == 0) // Wrapped around, palette full
                {
                    // Find nearest existing palette entry
                    palIdx = FindNearestPaletteEntry(colorToPalette, qr, qg, qb);
                }
                else
                {
                    palIdx = nextIndex;
                    palette.Set(palIdx, new MaterialDef
                    {
                        Name = $"color_{palIdx}",
                        Color = new RgbaColor(qr, qg, qb),
                    });
                    colorToPalette[key] = palIdx;
                    nextIndex++;
                }
            }

            model.SetVoxel(pos, palIdx);
        }

        _logger.LogInformation("Color palette: {Count} entries from {VoxelCount} voxels",
            palette.Count, voxelColors.Count);
    }

    private static byte FindNearestPaletteEntry(Dictionary<(byte r, byte g, byte b), byte> colorToPalette,
        byte r, byte g, byte b)
    {
        int bestDist = int.MaxValue;
        byte bestIdx = 1;
        foreach (var (key, idx) in colorToPalette)
        {
            int dr = r - key.r;
            int dg = g - key.g;
            int db = b - key.b;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = idx;
            }
        }
        return bestIdx;
    }

    // Map (axis, u, v, w) back to (x, y, z)
    private static int GetX(int axis, int u, int v, int w) => axis == 0 ? w : axis == 1 ? u : u;
    private static int GetY(int axis, int u, int v, int w) => axis == 0 ? u : axis == 1 ? w : v;
    private static int GetZ(int axis, int u, int v, int w) => axis == 0 ? v : axis == 1 ? v : w;

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection.
    /// </summary>
    internal static bool RayTriangleIntersect(
        Vector3 origin, Vector3 dir,
        Vector3 v0, Vector3 v1, Vector3 v2,
        out float t)
    {
        return RayTriangleIntersectBary(origin, dir, v0, v1, v2, out t, out _, out _);
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection with barycentric coordinates.
    /// baryU/baryV correspond to v1/v2 weights; v0 weight = 1 - baryU - baryV.
    /// </summary>
    internal static bool RayTriangleIntersectBary(
        Vector3 origin, Vector3 dir,
        Vector3 v0, Vector3 v1, Vector3 v2,
        out float t, out float baryU, out float baryV)
    {
        t = 0;
        baryU = 0;
        baryV = 0;
        const float epsilon = 1e-6f;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3.Cross(dir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (MathF.Abs(a) < epsilon)
            return false;

        float f = 1f / a;
        var s = origin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0f || u > 1f)
            return false;

        var q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(dir, q);

        if (v < 0f || u + v > 1f)
            return false;

        t = f * Vector3.Dot(edge2, q);
        if (t > epsilon)
        {
            baryU = u;
            baryV = v;
            return true;
        }
        return false;
    }
}
