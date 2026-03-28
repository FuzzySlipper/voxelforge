using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;
using VoxelForge.Engine.MonoGame.Rendering;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders reference models loaded via AssimpNetter. Draws after voxels.
/// </summary>
public sealed class ReferenceModelRenderer : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ReferenceModelRegistry _registry;
    private readonly List<GpuMesh> _gpuMeshes = [];
    private bool _dirty = true;

    public ReferenceModelRenderer(GraphicsDevice graphicsDevice, ReferenceModelRegistry registry)
    {
        _graphicsDevice = graphicsDevice;
        _registry = registry;
        _registry.Changed += () => _dirty = true;
    }

    public void Draw(OrbitalCamera camera, BasicEffect effect)
    {
        if (_dirty)
        {
            RebuildBuffers();
            _dirty = false;
        }

        int meshIndex = 0;
        foreach (var model in _registry.Models)
        {
            if (!model.IsVisible) continue;

            var world = Matrix.CreateScale(model.Scale)
                * Matrix.CreateFromYawPitchRoll(
                    MathHelper.ToRadians(model.RotationY),
                    MathHelper.ToRadians(model.RotationX),
                    MathHelper.ToRadians(model.RotationZ))
                * Matrix.CreateTranslation(model.PositionX, model.PositionY, model.PositionZ);

            float aspect = _graphicsDevice.Viewport.AspectRatio;
            effect.World = world;
            effect.View = camera.GetView();
            effect.Projection = camera.GetProjection(aspect);
            effect.VertexColorEnabled = true;

            var previousRasterizer = _graphicsDevice.RasterizerState;
            var previousBlend = _graphicsDevice.BlendState;

            switch (model.RenderMode)
            {
                case ReferenceRenderMode.Wireframe:
                    effect.LightingEnabled = false;
                    _graphicsDevice.RasterizerState = new RasterizerState
                    {
                        FillMode = FillMode.WireFrame,
                        CullMode = CullMode.None,
                    };
                    break;
                case ReferenceRenderMode.Transparent:
                    effect.LightingEnabled = true;
                    effect.EnableDefaultLighting();
                    effect.Alpha = 0.4f;
                    _graphicsDevice.BlendState = BlendState.AlphaBlend;
                    _graphicsDevice.RasterizerState = new RasterizerState { CullMode = CullMode.None };
                    break;
                default: // Solid
                    effect.LightingEnabled = true;
                    effect.EnableDefaultLighting();
                    effect.Alpha = 1f;
                    break;
            }

            foreach (var mesh in model.Meshes)
            {
                if (meshIndex >= _gpuMeshes.Count) break;
                var gpu = _gpuMeshes[meshIndex];
                if (gpu.IndexCount > 0 && gpu.VertexBuffer is not null && gpu.IndexBuffer is not null)
                {
                    _graphicsDevice.SetVertexBuffer(gpu.VertexBuffer);
                    _graphicsDevice.Indices = gpu.IndexBuffer;

                    foreach (var pass in effect.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _graphicsDevice.DrawIndexedPrimitives(
                            PrimitiveType.TriangleList, 0, 0,
                            gpu.VertexBuffer.VertexCount, 0, gpu.IndexCount / 3);
                    }
                }
                meshIndex++;
            }

            _graphicsDevice.RasterizerState = previousRasterizer;
            _graphicsDevice.BlendState = previousBlend;
            effect.Alpha = 1f;
        }
    }

    private void RebuildBuffers()
    {
        foreach (var gpu in _gpuMeshes)
            gpu.Dispose();
        _gpuMeshes.Clear();

        foreach (var model in _registry.Models)
        {
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
                {
                    _gpuMeshes.Add(new GpuMesh());
                    continue;
                }

                var mgVerts = new VoxelVertexMg[mesh.Vertices.Length];
                for (int i = 0; i < mesh.Vertices.Length; i++)
                {
                    var v = mesh.Vertices[i];
                    mgVerts[i] = new VoxelVertexMg(
                        new Vector3(v.PosX, v.PosY, v.PosZ),
                        new Vector3(v.NormX, v.NormY, v.NormZ),
                        new Color(v.R, v.G, v.B, v.A));
                }

                var vb = new VertexBuffer(_graphicsDevice, VoxelVertexMg.Declaration,
                    mgVerts.Length, BufferUsage.WriteOnly);
                vb.SetData(mgVerts);

                var ib = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits,
                    mesh.Indices.Length, BufferUsage.WriteOnly);
                ib.SetData(mesh.Indices);

                _gpuMeshes.Add(new GpuMesh
                {
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    IndexCount = mesh.Indices.Length,
                });
            }
        }
    }

    public void Dispose()
    {
        foreach (var gpu in _gpuMeshes)
            gpu.Dispose();
    }

    private sealed class GpuMesh : IDisposable
    {
        public VertexBuffer? VertexBuffer { get; init; }
        public IndexBuffer? IndexBuffer { get; init; }
        public int IndexCount { get; init; }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}
