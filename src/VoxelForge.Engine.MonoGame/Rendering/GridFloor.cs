using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders a flat grid on the XZ plane for spatial reference.
/// Rebuild by calling <see cref="Resize"/> when the grid size changes.
/// </summary>
public sealed class GridFloor : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private VertexBuffer? _vertexBuffer;
    private int _lineCount;
    private int _halfExtent;

    public GridFloor(GraphicsDevice graphicsDevice, int halfExtent = 32)
    {
        _graphicsDevice = graphicsDevice;
        _halfExtent = halfExtent;
        Build();
    }

    public void Resize(int halfExtent)
    {
        if (halfExtent == _halfExtent)
            return;
        _halfExtent = halfExtent;
        _vertexBuffer?.Dispose();
        Build();
    }

    private void Build()
    {
        var lines = new List<VertexPositionColor>();
        var gridColor = new Color(80, 80, 80);

        for (int i = -_halfExtent; i <= _halfExtent; i++)
        {
            lines.Add(new VertexPositionColor(new Vector3(i, 0f, -_halfExtent), gridColor));
            lines.Add(new VertexPositionColor(new Vector3(i, 0f, _halfExtent), gridColor));

            lines.Add(new VertexPositionColor(new Vector3(-_halfExtent, 0f, i), gridColor));
            lines.Add(new VertexPositionColor(new Vector3(_halfExtent, 0f, i), gridColor));
        }

        _lineCount = lines.Count / 2;
        _vertexBuffer = new VertexBuffer(_graphicsDevice, VertexPositionColor.VertexDeclaration,
            lines.Count, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(lines.ToArray());
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect)
    {
        if (_vertexBuffer is null) return;

        effect.VertexColorEnabled = true;
        effect.LightingEnabled = false;

        graphicsDevice.SetVertexBuffer(_vertexBuffer);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, _lineCount);
        }
    }

    public void Dispose()
    {
        _vertexBuffer?.Dispose();
    }
}
