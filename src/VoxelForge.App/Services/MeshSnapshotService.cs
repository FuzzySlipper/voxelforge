using VoxelForge.App.Snapshots;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service that produces renderer-neutral mesh snapshots from a <see cref="VoxelModel"/>
/// using an <see cref="IVoxelMesher"/>. Does not hold mutable state or depend on rendering types.
/// <para>
/// The mesher is injected so callers can choose <see cref="GreedyMesher"/> (production)
/// or <see cref="NaiveMesher"/> (debug baseline).
/// </para>
/// </summary>
public sealed class MeshSnapshotService
{
    private readonly IVoxelMesher _mesher;

    public MeshSnapshotService(IVoxelMesher mesher)
    {
        ArgumentNullException.ThrowIfNull(mesher);
        _mesher = mesher;
    }

    /// <summary>
    /// Build a renderer-neutral mesh snapshot from the given model.
    /// Returns an empty snapshot if the model has no voxels.
    /// </summary>
    public MeshSnapshot BuildSnapshot(VoxelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var meshData = _mesher.Build(model);

        if (meshData.Vertices.Length == 0)
        {
            return EmptySnapshot();
        }

        var positions = new float[meshData.Vertices.Length * 3];
        var normals = new float[meshData.Vertices.Length * 3];
        var colors = new byte[meshData.Vertices.Length * 4];
        var paletteIndices = new byte[meshData.Vertices.Length];

        // Build a reverse color-to-palette-index map from the model's palette.
        // Both NaiveMesher and GreedyMesher assign per-vertex colors from
        // Palette.Get(paletteIndex).Color, so we can recover the index from color.
        var colorToIndex = BuildColorToPaletteIndexMap(model.Palette);

        for (int i = 0; i < meshData.Vertices.Length; i++)
        {
            var v = meshData.Vertices[i];
            positions[i * 3 + 0] = v.X;
            positions[i * 3 + 1] = v.Y;
            positions[i * 3 + 2] = v.Z;

            normals[i * 3 + 0] = v.NX;
            normals[i * 3 + 1] = v.NY;
            normals[i * 3 + 2] = v.NZ;

            colors[i * 4 + 0] = v.R;
            colors[i * 4 + 1] = v.G;
            colors[i * 4 + 2] = v.B;
            colors[i * 4 + 3] = v.A;

            // Recover palette index from vertex color using the reverse map.
            // Fallback: if two palette entries share the same color, the later one wins,
            // but both produce the same rendered color so this is semantically correct.
            uint colorKey = PackColor(v.R, v.G, v.B, v.A);
            paletteIndices[i] = colorToIndex.TryGetValue(colorKey, out byte pIdx) ? pIdx : (byte)0;
        }

        var indices = new int[meshData.Indices.Length];
        Array.Copy(meshData.Indices, indices, meshData.Indices.Length);

        BoundsSnapshot? bounds = null;
        if (meshData.Bounds is { } b)
        {
            bounds = new BoundsSnapshot
            {
                MinX = b.Min.X,
                MinY = b.Min.Y,
                MinZ = b.Min.Z,
                MaxX = b.Max.X,
                MaxY = b.Max.Y,
                MaxZ = b.Max.Z,
            };
        }

        return new MeshSnapshot
        {
            Positions = positions,
            Normals = normals,
            Colors = colors,
            PaletteIndices = paletteIndices,
            Indices = indices,
            Bounds = bounds,
        };
    }

    /// <summary>
    /// Build an empty mesh snapshot (zero vertices, zero indices, no palette indices).
    /// Useful for initial state before a model is loaded.
    /// </summary>
    public static MeshSnapshot EmptySnapshot()
    {
        return new MeshSnapshot
        {
            Positions = [],
            Normals = [],
            Colors = [],
            PaletteIndices = null,
            Indices = [],
            Bounds = null,
        };
    }

    /// <summary>
    /// Build a reverse color→palette-index map from a palette.
    /// If two palette entries share the same RGBA color, the later (higher-index
    /// iteration order) entry wins. Both produce the same rendered color, so this
    /// is semantically correct for rendering but means palette index recovery is
    /// ambiguous when duplicate colors exist. This is acceptable because the meshers
    /// produce vertex colors from palette entries and the resulting visuals are
    /// identical regardless of which index is recovered.
    /// </summary>
    private static Dictionary<uint, byte> BuildColorToPaletteIndexMap(Palette palette)
    {
        var map = new Dictionary<uint, byte>();
        foreach (var (idx, mat) in palette.Entries)
        {
            uint key = PackColor(mat.Color.R, mat.Color.G, mat.Color.B, mat.Color.A);
            map[key] = idx;
        }
        return map;
    }

    private static uint PackColor(byte r, byte g, byte b, byte a)
    {
        return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
    }
}