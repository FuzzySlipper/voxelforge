using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders a wireframe grid of 1-meter cubes for scale reference.
/// Each cube is <see cref="VoxelsPerMeter"/> voxel units on a side.
/// Toggle visibility with <see cref="IsVisible"/>.
/// </summary>
public sealed class MeasureGrid : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private VertexBuffer? _vertexBuffer;
    private int _lineCount;
    private float _voxelsPerMeter;
    private int _metersExtent;
    private int _metersHeight;

    public bool IsVisible { get; set; }

    public MeasureGrid(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Rebuild the grid when scale settings change.
    /// </summary>
    /// <param name="voxelsPerMeter">How many voxel units equal 1 meter.</param>
    /// <param name="metersExtent">Half-extent of the grid in meters on XZ.</param>
    /// <param name="metersHeight">Height of the grid in meters.</param>
    public void Rebuild(float voxelsPerMeter, int metersExtent = 4, int metersHeight = 4)
    {
        if (MathF.Abs(voxelsPerMeter - _voxelsPerMeter) < 0.001f &&
            metersExtent == _metersExtent && metersHeight == _metersHeight)
            return;

        _voxelsPerMeter = voxelsPerMeter;
        _metersExtent = metersExtent;
        _metersHeight = metersHeight;
        _vertexBuffer?.Dispose();
        Build();
    }

    private void Build()
    {
        float step = _voxelsPerMeter;
        if (step < 1f) return;

        var lines = new List<VertexPositionColor>();
        var color = new Color(100, 180, 255, 80); // Light blue, translucent
        var accentColor = new Color(255, 200, 80, 120); // Gold for 1-meter height marks

        float minX = -_metersExtent * step;
        float maxX = _metersExtent * step;
        float minZ = -_metersExtent * step;
        float maxZ = _metersExtent * step;
        float maxY = _metersHeight * step;

        // Vertical columns at each meter intersection
        for (int mx = -_metersExtent; mx <= _metersExtent; mx++)
        {
            for (int mz = -_metersExtent; mz <= _metersExtent; mz++)
            {
                float x = mx * step;
                float z = mz * step;
                lines.Add(new VertexPositionColor(new Vector3(x, 0, z), color));
                lines.Add(new VertexPositionColor(new Vector3(x, maxY, z), color));
            }
        }

        // Horizontal rings at each meter height
        for (int my = 0; my <= _metersHeight; my++)
        {
            float y = my * step;
            var c = my > 0 ? accentColor : color;

            // X-aligned lines
            for (int mz = -_metersExtent; mz <= _metersExtent; mz++)
            {
                float z = mz * step;
                lines.Add(new VertexPositionColor(new Vector3(minX, y, z), c));
                lines.Add(new VertexPositionColor(new Vector3(maxX, y, z), c));
            }

            // Z-aligned lines
            for (int mx = -_metersExtent; mx <= _metersExtent; mx++)
            {
                float x = mx * step;
                lines.Add(new VertexPositionColor(new Vector3(x, y, minZ), c));
                lines.Add(new VertexPositionColor(new Vector3(x, y, maxZ), c));
            }
        }

        _lineCount = lines.Count / 2;
        if (_lineCount == 0) return;

        _vertexBuffer = new VertexBuffer(_graphicsDevice, VertexPositionColor.VertexDeclaration,
            lines.Count, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(lines.ToArray());
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect)
    {
        if (!IsVisible || _vertexBuffer is null || _lineCount == 0) return;

        effect.VertexColorEnabled = true;
        effect.LightingEnabled = false;

        // Enable alpha blending for translucent lines
        var prevBlend = graphicsDevice.BlendState;
        graphicsDevice.BlendState = BlendState.AlphaBlend;
        graphicsDevice.SetVertexBuffer(_vertexBuffer);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, _lineCount);
        }

        graphicsDevice.BlendState = prevBlend;
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
    }
}
