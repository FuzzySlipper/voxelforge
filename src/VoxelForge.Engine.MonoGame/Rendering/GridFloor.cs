using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders a flat grid on the XZ plane for spatial reference.
/// </summary>
public sealed class GridFloor : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;
    private readonly int _lineCount;

    public GridFloor(GraphicsDevice graphicsDevice, int halfExtent = 32, float y = 0f)
    {
        var lines = new List<VertexPositionColor>();
        var gridColor = new Color(80, 80, 80);

        for (int i = -halfExtent; i <= halfExtent; i++)
        {
            // Lines along Z
            lines.Add(new VertexPositionColor(new Vector3(i, y, -halfExtent), gridColor));
            lines.Add(new VertexPositionColor(new Vector3(i, y, halfExtent), gridColor));

            // Lines along X
            lines.Add(new VertexPositionColor(new Vector3(-halfExtent, y, i), gridColor));
            lines.Add(new VertexPositionColor(new Vector3(halfExtent, y, i), gridColor));
        }

        _lineCount = lines.Count / 2;
        _vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionColor.VertexDeclaration,
            lines.Count, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(lines.ToArray());
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect)
    {
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
        _vertexBuffer.Dispose();
    }
}
