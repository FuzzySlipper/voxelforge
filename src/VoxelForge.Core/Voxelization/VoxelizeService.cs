using System.Numerics;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.Voxelization;

/// <summary>
/// Converts a triangle mesh into a VoxelModel by ray-based sampling.
/// Supports surface (shell only) and solid (filled interior) modes.
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

        var voxels = new bool[resolution, resolution, resolution];

        // Cast rays along each axis
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            Parallel.For(0, resolution, uIdx =>
            {
                var hits = new List<float>();

                for (int vIdx = 0; vIdx < resolution; vIdx++)
                {
                    hits.Clear();

                    // Build ray: origin at (u, v) cell center, direction along axis
                    var origin = new float[3];
                    origin[u] = min[u] + (uIdx + 0.5f) * cellSize;
                    origin[v] = min[v] + (vIdx + 0.5f) * cellSize;
                    origin[axis] = min[axis] - cellSize;

                    var rayOrigin = new Vector3(origin[0], origin[1], origin[2]);
                    var rayDir = new Vector3(
                        axis == 0 ? 1f : 0f,
                        axis == 1 ? 1f : 0f,
                        axis == 2 ? 1f : 0f);

                    // Test against all triangles
                    for (int t = 0; t < mesh.Indices.Length; t += 3)
                    {
                        var p0 = mesh.Positions[mesh.Indices[t]];
                        var p1 = mesh.Positions[mesh.Indices[t + 1]];
                        var p2 = mesh.Positions[mesh.Indices[t + 2]];

                        if (RayTriangleIntersect(rayOrigin, rayDir, p0, p1, p2, out float tHit))
                            hits.Add(tHit);
                    }

                    if (hits.Count == 0) continue;
                    hits.Sort();

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
            });
        }

        // Build VoxelModel from results
        var model = new VoxelModel(_loggerFactory.CreateLogger<VoxelModel>()) { GridHint = resolution };
        int count = 0;
        for (int x = 0; x < resolution; x++)
        for (int y = 0; y < resolution; y++)
        for (int z = 0; z < resolution; z++)
        {
            if (voxels[x, y, z])
            {
                model.SetVoxel(new Point3(x, y, z), paletteIndex);
                count++;
            }
        }

        _logger.LogInformation("Voxelization complete: {Count} voxels", count);
        return model;
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
        t = 0;
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
        return t > epsilon;
    }
}
