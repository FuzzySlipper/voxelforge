using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// MonoGame vertex type matching VoxelForge.Core.Meshing.VoxelVertex layout.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VoxelVertexMg : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0));

    public readonly VertexDeclaration VertexDeclaration => Declaration;

    public VoxelVertexMg(Vector3 position, Vector3 normal, Color color)
    {
        Position = position;
        Normal = normal;
        Color = color;
    }
}
