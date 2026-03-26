namespace VoxelForge.Core.Meshing;

/// <summary>
/// Greedy meshing: merges adjacent coplanar faces of the same material into larger quads.
/// Standard algorithm — sweeps each axis, builds 2D masks, greedily expands rectangles.
/// </summary>
public sealed class GreedyMesher : IVoxelMesher
{
    public VoxelMesh Build(VoxelModel model)
    {
        if (model.GetVoxelCount() == 0)
            return new VoxelMesh();

        var bounds = model.GetBounds();
        if (bounds is null)
            return new VoxelMesh();

        var (min, max) = bounds.Value;
        var vertices = new List<VoxelVertex>();
        var indices = new List<int>();

        // For each of the 3 axes and 2 directions (positive/negative face)
        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;

            int[] dims = [max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1];
            int[] offset = [min.X, min.Y, min.Z];

            for (int sign = -1; sign <= 1; sign += 2)
            {
                // Sweep slices perpendicular to the axis
                // Include one extra slice for positive faces at the boundary
                int sliceStart = 0;
                int sliceEnd = dims[axis];

                for (int d = sliceStart; d <= sliceEnd; d++)
                {
                    // Build mask for this slice
                    int uSize = dims[u];
                    int vSize = dims[v];
                    // mask[i,j] = paletteIndex of exposed face, 0 = no face
                    var mask = new byte[uSize * vSize];
                    var hasFace = new bool[uSize * vSize];

                    for (int j = 0; j < vSize; j++)
                    {
                        for (int i = 0; i < uSize; i++)
                        {
                            int[] pos = new int[3];
                            int[] neighborPos = new int[3];

                            pos[axis] = d + offset[axis];
                            pos[u] = i + offset[u];
                            pos[v] = j + offset[v];

                            neighborPos[axis] = pos[axis] + sign;
                            neighborPos[u] = pos[u];
                            neighborPos[v] = pos[v];

                            var currentPos = new Point3(pos[0], pos[1], pos[2]);
                            var adjPos = new Point3(neighborPos[0], neighborPos[1], neighborPos[2]);

                            byte? current;
                            byte? adjacent;

                            if (sign > 0)
                            {
                                // Positive face: face is on the current voxel, looking outward
                                current = model.GetVoxel(currentPos);
                                adjacent = model.GetVoxel(adjPos);
                            }
                            else
                            {
                                // Negative face: face is on the adjacent voxel, looking inward
                                // We want the face of the voxel at pos, facing in -axis direction
                                current = model.GetVoxel(currentPos);
                                adjacent = model.GetVoxel(adjPos);
                            }

                            // Face exists if current voxel is solid and neighbor is air
                            if (current.HasValue && !adjacent.HasValue)
                            {
                                mask[j * uSize + i] = current.Value;
                                hasFace[j * uSize + i] = true;
                            }
                        }
                    }

                    // Greedy merge the mask into rectangles
                    var visited = new bool[uSize * vSize];

                    for (int j = 0; j < vSize; j++)
                    {
                        for (int i = 0; i < uSize; i++)
                        {
                            int idx = j * uSize + i;
                            if (!hasFace[idx] || visited[idx])
                                continue;

                            byte material = mask[idx];

                            // Expand width (along u)
                            int w = 1;
                            while (i + w < uSize)
                            {
                                int nextIdx = j * uSize + (i + w);
                                if (!hasFace[nextIdx] || visited[nextIdx] || mask[nextIdx] != material)
                                    break;
                                w++;
                            }

                            // Expand height (along v)
                            int h = 1;
                            bool canExpand = true;
                            while (canExpand && j + h < vSize)
                            {
                                for (int wi = 0; wi < w; wi++)
                                {
                                    int checkIdx = (j + h) * uSize + (i + wi);
                                    if (!hasFace[checkIdx] || visited[checkIdx] || mask[checkIdx] != material)
                                    {
                                        canExpand = false;
                                        break;
                                    }
                                }
                                if (canExpand) h++;
                            }

                            // Mark visited
                            for (int vj = 0; vj < h; vj++)
                            for (int ui = 0; ui < w; ui++)
                                visited[(j + vj) * uSize + (i + ui)] = true;

                            // Emit quad
                            var mat = model.Palette.Get(material);
                            byte cr = mat?.Color.R ?? RgbaColor.Magenta.R;
                            byte cg = mat?.Color.G ?? RgbaColor.Magenta.G;
                            byte cb = mat?.Color.B ?? RgbaColor.Magenta.B;
                            byte ca = mat?.Color.A ?? RgbaColor.Magenta.A;

                            float nx = 0, ny = 0, nz = 0;
                            if (axis == 0) nx = sign;
                            else if (axis == 1) ny = sign;
                            else nz = sign;

                            // Compute quad corners in world space
                            float[] corner = new float[3];
                            corner[axis] = d + offset[axis] + (sign > 0 ? 1f : 0f);
                            corner[u] = i + offset[u];
                            corner[v] = j + offset[v];

                            float[] c0 = [corner[0], corner[1], corner[2]];
                            float[] c1 = [corner[0], corner[1], corner[2]];
                            float[] c2 = [corner[0], corner[1], corner[2]];
                            float[] c3 = [corner[0], corner[1], corner[2]];

                            c1[u] += w;
                            c2[u] += w;
                            c2[v] += h;
                            c3[v] += h;

                            int baseIdx = vertices.Count;
                            vertices.Add(new VoxelVertex(c0[0], c0[1], c0[2], nx, ny, nz, cr, cg, cb, ca));
                            vertices.Add(new VoxelVertex(c1[0], c1[1], c1[2], nx, ny, nz, cr, cg, cb, ca));
                            vertices.Add(new VoxelVertex(c2[0], c2[1], c2[2], nx, ny, nz, cr, cg, cb, ca));
                            vertices.Add(new VoxelVertex(c3[0], c3[1], c3[2], nx, ny, nz, cr, cg, cb, ca));

                            if (sign > 0)
                            {
                                indices.Add(baseIdx);
                                indices.Add(baseIdx + 1);
                                indices.Add(baseIdx + 2);
                                indices.Add(baseIdx);
                                indices.Add(baseIdx + 2);
                                indices.Add(baseIdx + 3);
                            }
                            else
                            {
                                indices.Add(baseIdx);
                                indices.Add(baseIdx + 2);
                                indices.Add(baseIdx + 1);
                                indices.Add(baseIdx);
                                indices.Add(baseIdx + 3);
                                indices.Add(baseIdx + 2);
                            }
                        }
                    }
                }
            }
        }

        return new VoxelMesh
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Bounds = bounds,
        };
    }
}
