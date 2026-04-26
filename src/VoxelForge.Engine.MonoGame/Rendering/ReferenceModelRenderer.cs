using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;
using VoxelForge.Engine.MonoGame.Rendering;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders reference models loaded via AssimpNetter. Draws after voxels.
/// Supports per-frame CPU skeletal skinning for animated models.
/// </summary>
public sealed class ReferenceModelRenderer : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ReferenceModelState _referenceModelState;
    private readonly List<GpuMesh> _gpuMeshes = [];
    private bool _dirty = true;

    public ReferenceModelRenderer(GraphicsDevice graphicsDevice, ReferenceModelState referenceModelState)
    {
        _graphicsDevice = graphicsDevice;
        _referenceModelState = referenceModelState;
        _referenceModelState.Changed += () => _dirty = true;
    }

    /// <summary>
    /// Tick animation time on all animating models. Call from Update().
    /// </summary>
    public void UpdateAnimations(float deltaSeconds)
    {
        foreach (var model in _referenceModelState.Models)
            model.UpdateAnimation(deltaSeconds);
    }

    public void Draw(OrbitalCamera camera, BasicEffect effect)
    {
        if (_dirty)
        {
            RebuildBuffers();
            _dirty = false;
        }

        // Update dynamic vertex buffers for animated models
        UpdateAnimatedBuffers();

        int meshIndex = 0;
        foreach (var model in _referenceModelState.Models)
        {
            if (!model.IsVisible)
            {
                meshIndex += model.Meshes.Count;
                continue;
            }

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
                    // Both voxel and reference meshes use CCW front-facing with CullClockwise.
                    _graphicsDevice.RasterizerState = RasterizerState.CullClockwise;
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

    /// <summary>
    /// Recompute skinned vertices for any model that is currently animating.
    /// </summary>
    private void UpdateAnimatedBuffers()
    {
        int meshIndex = 0;
        foreach (var model in _referenceModelState.Models)
        {
            bool animating = model.IsAnimating
                && model.ActiveClipIndex is { } clipIdx
                && model.Skeleton is not null
                && model.AnimationClips is not null
                && clipIdx >= 0 && clipIdx < model.AnimationClips.Count;

            foreach (var mesh in model.Meshes)
            {
                if (meshIndex >= _gpuMeshes.Count) break;
                var gpu = _gpuMeshes[meshIndex];

                if (animating && gpu.IsDynamic && gpu.DynamicVerts is not null && gpu.VertexBuffer is not null)
                {
                    var clip = model.AnimationClips![model.ActiveClipIndex!.Value];
                    var boneMatrices = SkeletalAnimator.ComputeBoneMatrices(model.Skeleton!, clip, model.AnimationTime);

                    int vertCount = mesh.Vertices.Length;
                    gpu.SkinPositions ??= new float[vertCount * 3];
                    gpu.SkinNormals ??= new float[vertCount * 3];

                    SkeletalAnimator.SkinVertices(mesh.Vertices, boneMatrices, gpu.SkinPositions, gpu.SkinNormals);

                    // Write skinned positions/normals into the staging array
                    for (int i = 0; i < vertCount; i++)
                    {
                        int o = i * 3;
                        gpu.DynamicVerts[i] = new VoxelVertexMg(
                            new Vector3(gpu.SkinPositions[o], gpu.SkinPositions[o + 1], gpu.SkinPositions[o + 2]),
                            new Vector3(gpu.SkinNormals[o], gpu.SkinNormals[o + 1], gpu.SkinNormals[o + 2]),
                            gpu.DynamicVerts[i].Color);
                    }

                    gpu.VertexBuffer.SetData(gpu.DynamicVerts);
                }

                meshIndex++;
            }
        }
    }

    private void RebuildBuffers()
    {
        foreach (var gpu in _gpuMeshes)
            gpu.Dispose();
        _gpuMeshes.Clear();

        foreach (var model in _referenceModelState.Models)
        {
            bool hasAnimation = model.HasAnimations;

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

                // Use dynamic buffer for animated models so we can update per-frame
                var usage = hasAnimation ? BufferUsage.None : BufferUsage.WriteOnly;
                var vb = new VertexBuffer(_graphicsDevice, VoxelVertexMg.Declaration,
                    mgVerts.Length, usage);
                vb.SetData(mgVerts);

                var ib = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits,
                    mesh.Indices.Length, BufferUsage.WriteOnly);
                ib.SetData(mesh.Indices);

                _gpuMeshes.Add(new GpuMesh
                {
                    VertexBuffer = vb,
                    IndexBuffer = ib,
                    IndexCount = mesh.Indices.Length,
                    IsDynamic = hasAnimation,
                    DynamicVerts = hasAnimation ? mgVerts : null,
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

        // Animation support: dynamic buffers for skinned models
        public bool IsDynamic { get; init; }
        public VoxelVertexMg[]? DynamicVerts { get; init; }
        public float[]? SkinPositions { get; set; }
        public float[]? SkinNormals { get; set; }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
        }
    }
}
