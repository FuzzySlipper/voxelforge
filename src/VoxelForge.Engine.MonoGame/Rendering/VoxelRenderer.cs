using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders a VoxelModel using a mesher, with dirty-flag driven buffer rebuilds.
/// </summary>
public sealed class VoxelRenderer : IDisposable
{
    private readonly IVoxelMesher _mesher;
    private readonly GraphicsDevice _graphicsDevice;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private int _indexCount;
    private bool _dirty = true;

    public bool WireframeEnabled { get; set; }

    public VoxelRenderer(IVoxelMesher mesher, GraphicsDevice graphicsDevice)
    {
        _mesher = mesher;
        _graphicsDevice = graphicsDevice;
    }

    public void MarkDirty() => _dirty = true;

    public void Draw(VoxelModel model, OrbitalCamera camera, BasicEffect effect)
    {
        if (_dirty)
        {
            RebuildBuffers(model);
            _dirty = false;
        }

        if (_vertexBuffer is null || _indexBuffer is null || _indexCount == 0)
            return;

        float aspect = _graphicsDevice.Viewport.AspectRatio;
        effect.World = Matrix.Identity;
        effect.View = camera.GetView();
        effect.Projection = camera.GetProjection(aspect);
        effect.VertexColorEnabled = true;
        effect.LightingEnabled = true;
        effect.EnableDefaultLighting();

        var previousRasterizer = _graphicsDevice.RasterizerState;
        if (WireframeEnabled)
        {
            _graphicsDevice.RasterizerState = new RasterizerState
            {
                FillMode = FillMode.WireFrame,
                CullMode = CullMode.None,
            };
        }
        else
        {
            _graphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        }

        _graphicsDevice.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphicsDevice.DrawIndexedPrimitives(
                PrimitiveType.TriangleList, 0, 0,
                _vertexBuffer.VertexCount, 0, _indexCount / 3);
        }

        _graphicsDevice.RasterizerState = previousRasterizer;
    }

    private void RebuildBuffers(VoxelModel model)
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer = null;
        _indexCount = 0;

        var mesh = _mesher.Build(model);
        if (mesh.Vertices.Length == 0)
            return;

        // Convert Core vertices to MonoGame vertices
        var mgVertices = new VoxelVertexMg[mesh.Vertices.Length];
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var v = mesh.Vertices[i];
            mgVertices[i] = new VoxelVertexMg(
                new Vector3(v.X, v.Y, v.Z),
                new Vector3(v.NX, v.NY, v.NZ),
                new Color(v.R, v.G, v.B, v.A));
        }

        _vertexBuffer = new VertexBuffer(_graphicsDevice, VoxelVertexMg.Declaration,
            mgVertices.Length, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(mgVertices);

        _indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits,
            mesh.Indices.Length, BufferUsage.WriteOnly);
        _indexBuffer.SetData(mesh.Indices);

        _indexCount = mesh.Indices.Length;
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
    }
}
