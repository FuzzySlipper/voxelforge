namespace VoxelForge.Core.Meshing;

/// <summary>
/// Simple mesher: one quad per exposed face. Correct but unoptimized — useful as a baseline.
/// </summary>
public sealed class NaiveMesher : IVoxelMesher
{
    private static readonly (int Dx, int Dy, int Dz, int Axis, int Sign)[] Faces =
    [
        ( 1,  0,  0, 0,  1), // +X
        (-1,  0,  0, 0, -1), // -X
        ( 0,  1,  0, 1,  1), // +Y
        ( 0, -1,  0, 1, -1), // -Y
        ( 0,  0,  1, 2,  1), // +Z
        ( 0,  0, -1, 2, -1), // -Z
    ];

    public VoxelMesh Build(VoxelModel model)
    {
        if (model.GetVoxelCount() == 0)
            return new VoxelMesh();

        var vertices = new List<VoxelVertex>();
        var indices = new List<int>();

        foreach (var (pos, paletteIndex) in model.Voxels)
        {
            var mat = model.Palette.Get(paletteIndex);
            byte r = mat?.Color.R ?? RgbaColor.Magenta.R;
            byte g = mat?.Color.G ?? RgbaColor.Magenta.G;
            byte b = mat?.Color.B ?? RgbaColor.Magenta.B;
            byte a = mat?.Color.A ?? RgbaColor.Magenta.A;

            foreach (var (dx, dy, dz, axis, sign) in Faces)
            {
                var neighbor = new Point3(pos.X + dx, pos.Y + dy, pos.Z + dz);
                if (model.GetVoxel(neighbor) is not null)
                    continue; // Neighbor is solid — face is interior, skip

                float nx = dx, ny = dy, nz = dz;
                EmitQuad(vertices, indices, pos, nx, ny, nz, axis, sign, r, g, b, a);
            }
        }

        return new VoxelMesh
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Bounds = model.GetBounds(),
        };
    }

    private static void EmitQuad(
        List<VoxelVertex> vertices, List<int> indices,
        Point3 pos, float nx, float ny, float nz,
        int axis, int sign,
        byte r, byte g, byte b, byte a)
    {
        int baseIndex = vertices.Count;

        // Build 4 corner vertices for the quad on the exposed face.
        // The face sits on the boundary of the voxel cube [pos, pos+1).
        // For a positive-direction face, the quad is at pos+1 on that axis.
        // For a negative-direction face, the quad is at pos on that axis.
        float fx = pos.X, fy = pos.Y, fz = pos.Z;

        // The two tangent axes (u, v) perpendicular to the face normal
        // axis: 0=X, 1=Y, 2=Z
        // For +X face: quad at x=fx+1, spans (y,z)
        // For -X face: quad at x=fx,   spans (y,z)
        // For +Y face: quad at y=fy+1, spans (x,z)
        // etc.
        float[] corner = [fx, fy, fz];

        // Offset to the face plane
        if (sign > 0)
            corner[axis] += 1f;

        int u = (axis + 1) % 3;
        int v = (axis + 2) % 3;

        // 4 corners: (0,0), (1,0), (1,1), (0,1) in (u,v) space
        float[] c0 = [corner[0], corner[1], corner[2]];
        float[] c1 = [corner[0], corner[1], corner[2]];
        float[] c2 = [corner[0], corner[1], corner[2]];
        float[] c3 = [corner[0], corner[1], corner[2]];

        c1[u] += 1f;
        c2[u] += 1f;
        c2[v] += 1f;
        c3[v] += 1f;

        vertices.Add(new VoxelVertex(c0[0], c0[1], c0[2], nx, ny, nz, r, g, b, a));
        vertices.Add(new VoxelVertex(c1[0], c1[1], c1[2], nx, ny, nz, r, g, b, a));
        vertices.Add(new VoxelVertex(c2[0], c2[1], c2[2], nx, ny, nz, r, g, b, a));
        vertices.Add(new VoxelVertex(c3[0], c3[1], c3[2], nx, ny, nz, r, g, b, a));

        // Two triangles — winding depends on face direction
        // FNA default: CullCounterClockwiseFace — clockwise = front face
        if (sign > 0)
        {
            indices.Add(baseIndex);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }
        else
        {
            indices.Add(baseIndex);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex);
            indices.Add(baseIndex + 3);
            indices.Add(baseIndex + 2);
        }
    }
}
