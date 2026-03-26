using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Renders RGB axis lines (R=X, G=Y, B=Z) at the world origin.
/// </summary>
public sealed class AxisIndicator : IDisposable
{
    private readonly VertexBuffer _vertexBuffer;

    public AxisIndicator(GraphicsDevice graphicsDevice, float length = 3f)
    {
        var lines = new VertexPositionColor[]
        {
            // X axis — Red
            new(Vector3.Zero, Color.Red),
            new(new Vector3(length, 0, 0), Color.Red),
            // Y axis — Green
            new(Vector3.Zero, Color.Green),
            new(new Vector3(0, length, 0), Color.Green),
            // Z axis — Blue
            new(Vector3.Zero, Color.Blue),
            new(new Vector3(0, 0, length), Color.Blue),
        };

        _vertexBuffer = new VertexBuffer(graphicsDevice, VertexPositionColor.VertexDeclaration,
            lines.Length, BufferUsage.WriteOnly);
        _vertexBuffer.SetData(lines);
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect)
    {
        effect.VertexColorEnabled = true;
        effect.LightingEnabled = false;

        graphicsDevice.SetVertexBuffer(_vertexBuffer);

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, 3);
        }
    }

    public void Dispose()
    {
        _vertexBuffer.Dispose();
    }
}
