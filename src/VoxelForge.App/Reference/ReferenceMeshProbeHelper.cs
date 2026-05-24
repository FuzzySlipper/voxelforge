using System.Numerics;
using VoxelForge.Core.Reference;
using VoxelForge.Core.Voxelization;

namespace VoxelForge.App.Reference;

/// <summary>
/// CPU-side geometry probes for loaded reference meshes:
/// raycasting, orthographic silhouette sampling, and axis-aligned histogram.
/// Uses the same transform semantics as <see cref="ReferenceDiagnosticsHelper"/>
/// (Scale &rarr; YawPitchRoll &rarr; Translation).
/// </summary>
public static class ReferenceMeshProbeHelper
{
    // --------------- Public result types ---------------

    public sealed record RaycastHitResult(
        int MeshIndex,
        int TriangleIndex,
        float Distance,
        Vector3 Point,
        Vector3 Normal,
        int MaterialIndex = 0,
        string? MaterialName = null,
        byte R = 255, byte G = 255, byte B = 255);

    public sealed record RaycastBatchResult(
        int HitCount,
        List<RaycastHitResult>? Hits,
        int TotalMeshes,
        int TotalTriangles);

    public sealed record ProbeViewResult(
        string ViewName,
        Vector3 ViewAxis,
        Vector3 UpAxis,
        Vector3 ProjectionMin,
        Vector3 ProjectionMax,
        int Resolution,
        int OccupiedSamples,
        float OccupancyDensity,
        List<string>? RunLengthRows,
        float DepthMin,
        float DepthMax,
        float? MedianDepth);

    public sealed record ProbeBatchResult(
        List<ProbeViewResult> Views,
        int ViewCount,
        int TotalOccupiedSamples);

    public sealed record AxisHistogramResult(
        string Axis,
        int BinCount,
        float BinWidth,
        float MinValue,
        float MaxValue,
        int[] Counts,
        float? Mean,
        float? Median,
        int TotalSamples);

    // --------------- Constants ---------------

    /// <summary>Maximum ray hits per batch to prevent runaway output.</summary>
    public const int MaxRaycastHits = 1000;
    /// <summary>Maximum orthographic resolution per axis.</summary>
    public const int MaxOrthoResolution = 128;
    /// <summary>Maximum histogram bins.</summary>
    public const int MaxHistogramBins = 256;
    /// <summary>Maximum views per batch.</summary>
    public const int MaxViewCount = 6;

    /// <summary>Canonical view definitions: (name, look-axis, up-axis).</summary>
    private static readonly (string Name, Vector3 Look, Vector3 Up)[] CanonicalViews =
    [
        ("front",  new Vector3(0, 0, -1), new Vector3(0, 1, 0)),
        ("right",  new Vector3(1, 0, 0),  new Vector3(0, 1, 0)),
        ("top",    new Vector3(0, -1, 0), new Vector3(0, 0, 1)),
        ("back",   new Vector3(0, 0, 1),  new Vector3(0, 1, 0)),
        ("left",   new Vector3(-1, 0, 0), new Vector3(0, 1, 0)),
        ("bottom", new Vector3(0, 1, 0),  new Vector3(0, 0, 1)),
    ];

    // --------------- Public API ---------------

    /// <summary>
    /// Raycast against all mesh triangles (transformed or local space).
    /// Returns compact batch result with nearest hits, capped at <paramref name="maxHits"/>.
    /// </summary>
    public static RaycastBatchResult RaycastReferenceModel(
        ReferenceModelData model,
        Vector3 origin,
        Vector3 direction,
        int maxHits = 10,
        bool transformed = true)
    {
        // Validate direction
        float dirLen = direction.Length();
        if (dirLen < float.Epsilon)
            return new RaycastBatchResult(0, null, model.Meshes.Count, model.TotalTriangles);

        direction /= dirLen; // Normalize

        int cappedHits = Math.Clamp(maxHits, 1, MaxRaycastHits);

        Matrix4x4? worldToLocal = null;
        Matrix4x4? localToWorld = null;
        if (transformed)
        {
            localToWorld = ReferenceDiagnosticsHelper.BuildTransform(model);
            if (!Matrix4x4.Invert(localToWorld.Value, out var inv))
                return new RaycastBatchResult(-1, null, model.Meshes.Count, model.TotalTriangles);
            worldToLocal = inv;
        }

        var hits = new List<(float t, int meshIdx, int triBase, Vector3 bary, Vector3 point, Vector3 normal)>();

        for (int meshIdx = 0; meshIdx < model.Meshes.Count; meshIdx++)
        {
            var mesh = model.Meshes[meshIdx];
            var verts = mesh.Vertices;
            var indices = mesh.Indices;

            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];

                Vector3 GetPos(int idx) => new(verts[idx].PosX, verts[idx].PosY, verts[idx].PosZ);
                Vector3 GetNorm(int idx) => new(verts[idx].NormX, verts[idx].NormY, verts[idx].NormZ);

                Vector3 p0, p1, p2;

                if (transformed && localToWorld.HasValue)
                {
                    p0 = Vector3.Transform(GetPos(i0), localToWorld.Value);
                    p1 = Vector3.Transform(GetPos(i1), localToWorld.Value);
                    p2 = Vector3.Transform(GetPos(i2), localToWorld.Value);
                }
                else
                {
                    p0 = GetPos(i0);
                    p1 = GetPos(i1);
                    p2 = GetPos(i2);
                }

                if (VoxelizeService.RayTriangleIntersectBary(origin, direction, p0, p1, p2, out float tHit, out float baryU, out float baryV))
                {
                    Vector3 hitPoint = origin + direction * tHit;

                    // Interpolate normal
                    Vector3 n0, n1, n2;
                    if (transformed && localToWorld.HasValue)
                    {
                        // Transform normals by the inverse-transpose of localToWorld
                        n0 = Vector3.Normalize(Vector3.TransformNormal(GetNorm(i0), localToWorld.Value));
                        n1 = Vector3.Normalize(Vector3.TransformNormal(GetNorm(i1), localToWorld.Value));
                        n2 = Vector3.Normalize(Vector3.TransformNormal(GetNorm(i2), localToWorld.Value));
                    }
                    else
                    {
                        n0 = Vector3.Normalize(GetNorm(i0));
                        n1 = Vector3.Normalize(GetNorm(i1));
                        n2 = Vector3.Normalize(GetNorm(i2));
                    }

                    float baryW = 1f - baryU - baryV;
                    Vector3 hitNormal = Vector3.Normalize(n0 * baryW + n1 * baryU + n2 * baryV);

                    hits.Add((tHit, meshIdx, i, new Vector3(baryU, baryV, baryW), hitPoint, hitNormal));
                }
            }
        }

        if (hits.Count == 0)
            return new RaycastBatchResult(0, null, model.Meshes.Count, model.TotalTriangles);

        // Sort by distance and take nearest N
        hits.Sort((a, b) => a.t.CompareTo(b.t));
        if (hits.Count > cappedHits)
            hits = hits.GetRange(0, cappedHits);

        var resultHits = hits.Select(h =>
        {
            var mesh = model.Meshes[h.meshIdx];
            var verts = mesh.Vertices;
            int triVertIdx = h.triBase;
            return new RaycastHitResult(
                h.meshIdx,
                h.triBase / 3,
                h.t,
                h.point,
                h.normal,
                0,
                mesh.MaterialName,
                verts[mesh.Indices[triVertIdx]].R,
                verts[mesh.Indices[triVertIdx]].G,
                verts[mesh.Indices[triVertIdx]].B);
        }).ToList();

        return new RaycastBatchResult(resultHits.Count, resultHits, model.Meshes.Count, model.TotalTriangles);
    }

    /// <summary>
    /// Orthographic silhouette/depth probes of the transformed mesh from
    /// one or more canonical view directions. Returns compact run-length-encoded
    /// occupancy rows and depth ranges. Avoids huge full-resolution JSON output.
    ///
    /// Uses ray-casting per cell center rather than vertex-only projection,
    /// producing filled silhouettes for solid geometry (e.g., a cube yields
    /// a filled square, not just corner dots).
    /// </summary>
    public static ProbeBatchResult SampleReferenceModelViews(
        ReferenceModelData model,
        string[]? views = null,
        int resolution = 64)
    {
        int cappedRes = Math.Clamp(resolution, 1, MaxOrthoResolution);
        var selectedViews = views is { Length: > 0 }
            ? CanonicalViews.Where(v => views.Contains(v.Name, StringComparer.OrdinalIgnoreCase)).Take(MaxViewCount).ToList()
            : CanonicalViews.Take(3).ToList(); // default: front, right, top

        if (selectedViews.Count == 0)
            selectedViews = CanonicalViews.Take(3).ToList();

        var transform = ReferenceDiagnosticsHelper.BuildTransform(model);

        // Compute transformed AABB for projection bounds
        var aabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
        var aabbSize = aabb.Max - aabb.Min;
        float maxDim = MathF.Max(aabbSize.X, MathF.Max(aabbSize.Y, aabbSize.Z));
        if (maxDim < float.Epsilon)
            maxDim = 1f;

        // Gather all transformed triangles once (mesh index → tri list with transformed verts)
        var allTris = new List<(Vector3 P0, Vector3 P1, Vector3 P2)>();
        foreach (var mesh in model.Meshes)
        {
            var verts = mesh.Vertices;
            var indices = mesh.Indices;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
                allTris.Add((
                    Vector3.Transform(new Vector3(verts[i0].PosX, verts[i0].PosY, verts[i0].PosZ), transform),
                    Vector3.Transform(new Vector3(verts[i1].PosX, verts[i1].PosY, verts[i1].PosZ), transform),
                    Vector3.Transform(new Vector3(verts[i2].PosX, verts[i2].PosY, verts[i2].PosZ), transform)
                ));
            }
        }

        var viewResults = new List<ProbeViewResult>();
        int totalOccupied = 0;

        foreach (var (viewName, lookDir, upDir) in selectedViews)
        {
            // Build orthographic basis: right = look × up, then re-up = right × look
            Vector3 look = Vector3.Normalize(lookDir);
            Vector3 up = Vector3.Normalize(upDir);
            Vector3 right = Vector3.Normalize(Vector3.Cross(look, up));
            up = Vector3.Normalize(Vector3.Cross(right, look)); // ensure orthonormal

            // Project all transformed vertices to find UV bounds in this view
            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;
            float minDepth = float.MaxValue, maxDepth = float.MinValue;

            foreach (var mesh in model.Meshes)
            {
                foreach (var v in mesh.Vertices)
                {
                    var worldPos = Vector3.Transform(new Vector3(v.PosX, v.PosY, v.PosZ), transform);
                    float u = Vector3.Dot(worldPos, right);
                    float vProj = Vector3.Dot(worldPos, up);
                    float depth = Vector3.Dot(worldPos, look);

                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                    if (vProj < minV) minV = vProj;
                    if (vProj > maxV) maxV = vProj;
                    if (depth < minDepth) minDepth = depth;
                    if (depth > maxDepth) maxDepth = depth;
                }
            }

            // Handle degenerate projections
            float uRange = maxU - minU;
            float vRange = maxV - minV;
            if (uRange < float.Epsilon) uRange = 1f;
            if (vRange < float.Epsilon) vRange = 1f;
            if (minDepth > maxDepth) { float tmp = minDepth; minDepth = maxDepth; maxDepth = tmp; }
            if (maxDepth - minDepth < float.Epsilon) maxDepth = minDepth + 1f;

            float cellU = uRange / cappedRes;
            float cellV = vRange / cappedRes;

            // Place orthographic ray origin behind the far side of the model
            // (relative to camera at -look*INF). Ray direction = -look (toward camera),
            // so rays enter the model at the far face and exit at the near face
            // capturing all geometry between.
            float depthSpan = maxDepth - minDepth;
            float originDepth = maxDepth + depthSpan;

            // Rasterize to occupancy grid via ray-casting per cell
            bool[,] occupied = new bool[cappedRes, cappedRes];
            float[,] depthGrid = new float[cappedRes, cappedRes];
            int[,] depthCount = new int[cappedRes, cappedRes];
            for (int u = 0; u < cappedRes; u++)
                for (int v = 0; v < cappedRes; v++)
                    depthGrid[u, v] = float.MaxValue;

            for (int ui = 0; ui < cappedRes; ui++)
            {
                for (int vi = 0; vi < cappedRes; vi++)
                {
                    // Cell center in UV space
                    float uVal = minU + (ui + 0.5f) * cellU;
                    float vVal = minV + (vi + 0.5f) * cellV;

                    // World-space ray origin on the projection plane behind the model
                    Vector3 rayOrigin = originDepth * look + uVal * right + vVal * up;
                    Vector3 rayDir = -look;

                    // Test against all transformed triangles, early-exit on first hit
                    bool cellHit = false;
                    float nearestHitDepth = float.MaxValue;

                    foreach (var (p0, p1, p2) in allTris)
                    {
                        if (VoxelizeService.RayTriangleIntersectBary(
                                rayOrigin, rayDir, p0, p1, p2,
                                out float tHit, out _, out _))
                        {
                            Vector3 hitPoint = rayOrigin + rayDir * tHit;
                            float hitDepth = Vector3.Dot(hitPoint, look);
                            if (hitDepth < nearestHitDepth)
                                nearestHitDepth = hitDepth;
                            cellHit = true;
                            break; // Early exit: one hit is enough for occupancy
                        }
                    }

                    if (cellHit)
                    {
                        occupied[ui, vi] = true;
                        depthGrid[ui, vi] = nearestHitDepth;
                        depthCount[ui, vi] = 1;
                    }
                }
            }

            // Compute run-length encoding for occupancy rows
            var rleRows = new List<string>();
            for (int vi = 0; vi < cappedRes; vi++)
            {
                var row = new char[cappedRes];
                for (int ui = 0; ui < cappedRes; ui++)
                    row[ui] = occupied[ui, vi] ? '1' : '0';
                // RLE: count consecutive runs
                var rle = new System.Text.StringBuilder();
                int runStart = 0;
                for (int ui = 1; ui <= cappedRes; ui++)
                {
                    if (ui == cappedRes || row[ui] != row[runStart])
                    {
                        if (row[runStart] == '1')
                            rle.Append($"{ui - runStart},");
                        else
                        {
                            if (rle.Length > 0 || runStart > 0)
                                rle.Append($"_{ui - runStart},");
                        }
                        runStart = ui;
                    }
                }
                if (rle.Length > 0)
                    rle.Length--; // trim trailing comma
                rleRows.Add(rle.Length > 0 ? rle.ToString() : "_");
            }

            int occupiedCount = 0;
            float totalDepth = 0;
            int depthSamples = 0;
            var depthValues = new List<float>();
            for (int u = 0; u < cappedRes; u++)
            {
                for (int v = 0; v < cappedRes; v++)
                {
                    if (occupied[u, v])
                    {
                        occupiedCount++;
                        if (depthGrid[u, v] < float.MaxValue)
                        {
                            totalDepth += depthGrid[u, v];
                            depthSamples++;
                            depthValues.Add(depthGrid[u, v]);
                        }
                    }
                }
            }

            depthValues.Sort();
            float probeDepthMin = depthSamples > 0 ? depthValues[0] : 0;
            float probeDepthMax = depthSamples > 0 ? depthValues[^1] : 0;
            float? medianDepth = depthSamples > 0
                ? depthValues[depthValues.Count / 2]
                : null;

            float density = (float)occupiedCount / (cappedRes * cappedRes);
            totalOccupied += occupiedCount;

            viewResults.Add(new ProbeViewResult(
                viewName,
                look,
                up,
                new Vector3(minU, minV, 0),
                new Vector3(maxU, maxV, 0),
                cappedRes,
                occupiedCount,
                density,
                rleRows,
                probeDepthMin,
                probeDepthMax,
                medianDepth));
        }

        return new ProbeBatchResult(viewResults, viewResults.Count, totalOccupied);
    }

    /// <summary>
    /// Compute an approximate distribution of mesh vertex positions along one
    /// axis (world-space when transformed=true). Compact output capped at
    /// <paramref name="bins"/> bins.
    /// </summary>
    public static AxisHistogramResult ComputeAxisHistogram(
        ReferenceModelData model,
        char axis,
        int bins = 32,
        bool transformed = true)
    {
        int cappedBins = Math.Clamp(bins, 1, MaxHistogramBins);
        int axisIdx = axis switch
        {
            'x' or 'X' => 0,
            'y' or 'Y' => 1,
            'z' or 'Z' => 2,
            _ => 1, // default Y
        };

        Matrix4x4? transform = transformed ? ReferenceDiagnosticsHelper.BuildTransform(model) : null;

        // Collect values
        var values = new List<float>();
        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                float val;
                if (transform.HasValue)
                {
                    var worldPos = Vector3.Transform(new Vector3(v.PosX, v.PosY, v.PosZ), transform.Value);
                    val = axisIdx switch { 0 => worldPos.X, 1 => worldPos.Y, _ => worldPos.Z };
                }
                else
                {
                    val = axisIdx switch { 0 => v.PosX, 1 => v.PosY, _ => v.PosZ };
                }
                values.Add(val);
            }
        }

        if (values.Count == 0)
        {
            return new AxisHistogramResult(
                axis.ToString().ToLowerInvariant(),
                cappedBins, 0, 0, 0,
                new int[cappedBins],
                null, null, 0);
        }

        float minVal = float.MaxValue, maxVal = float.MinValue;
        foreach (var val in values)
        {
            if (val < minVal) minVal = val;
            if (val > maxVal) maxVal = val;
        }

        float range = maxVal - minVal;
        if (range < float.Epsilon) range = 1f;
        float binWidth = range / cappedBins;

        var counts = new int[cappedBins];
        foreach (var val in values)
        {
            int idx = (int)((val - minVal) / range * cappedBins);
            idx = Math.Clamp(idx, 0, cappedBins - 1);
            counts[idx]++;
        }

        values.Sort();
        float mean = values.Sum() / values.Count;
        float median = values[values.Count / 2];

        return new AxisHistogramResult(
            axis.ToString().ToLowerInvariant(),
            cappedBins, binWidth, minVal, maxVal,
            counts, mean, median, values.Count);
    }
}
