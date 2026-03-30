using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Screen-space orientation gizmo (Unity-style). Renders RGB axis lines in the
/// upper-right corner that rotate with the camera to show current orientation.
/// R=X, G=Y, B=Z — matches the world-origin AxisIndicator convention.
/// </summary>
public sealed class OrientationGizmo : IDisposable
{
    private const int GizmoSize = 80;   // Viewport square size in pixels
    private const int Margin = 10;      // Margin from screen edge
    private const float AxisLength = 0.8f;

    private readonly VertexBuffer _lineBuffer;
    private readonly VertexBuffer _labelBuffer;
    private readonly int _labelVertexCount;

    public OrientationGizmo(GraphicsDevice graphicsDevice)
    {
        // Three axis lines from origin
        var lines = new VertexPositionColor[]
        {
            // X — Red
            new(Vector3.Zero, Color.Red),
            new(new Vector3(AxisLength, 0, 0), Color.Red),
            // Y — Green
            new(Vector3.Zero, Color.Green),
            new(new Vector3(0, AxisLength, 0), Color.Green),
            // Z — Blue
            new(Vector3.Zero, Color.Blue),
            new(new Vector3(0, 0, AxisLength), Color.Blue),
        };

        _lineBuffer = new VertexBuffer(graphicsDevice, VertexPositionColor.VertexDeclaration,
            lines.Length, BufferUsage.WriteOnly);
        _lineBuffer.SetData(lines);

        // Letter labels drawn as line segments at the tip of each axis
        var labels = BuildLabels();
        _labelVertexCount = labels.Length;
        _labelBuffer = new VertexBuffer(graphicsDevice, VertexPositionColor.VertexDeclaration,
            labels.Length, BufferUsage.WriteOnly);
        _labelBuffer.SetData(labels);
    }

    public void Draw(GraphicsDevice graphicsDevice, BasicEffect effect, OrbitalCamera camera)
    {
        // Save state
        var prevViewport = graphicsDevice.Viewport;
        var prevDepth = graphicsDevice.DepthStencilState;
        var prevWorld = effect.World;
        var prevView = effect.View;
        var prevProjection = effect.Projection;
        var prevVertexColor = effect.VertexColorEnabled;
        var prevLighting = effect.LightingEnabled;

        // Small viewport in upper-right corner
        int vpX = prevViewport.Width - GizmoSize - Margin;
        int vpY = Margin;
        graphicsDevice.Viewport = new Viewport(vpX, vpY, GizmoSize, GizmoSize);
        graphicsDevice.DepthStencilState = DepthStencilState.None;

        // View = camera rotation only (no translation) — we look at origin from a fixed distance
        float cosP = MathF.Cos(camera.Pitch);
        float sinP = MathF.Sin(camera.Pitch);
        float cosY = MathF.Cos(camera.Yaw);
        float sinY = MathF.Sin(camera.Yaw);

        float dist = 2.5f;
        var eye = new Vector3(
            cosP * sinY * dist,
            sinP * dist,
            cosP * cosY * dist);

        effect.World = Matrix.Identity;
        effect.View = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up);
        effect.Projection = Matrix.CreateOrthographic(2.8f, 2.8f, 0.1f, 10f);
        effect.VertexColorEnabled = true;
        effect.LightingEnabled = false;

        // Draw axis lines
        graphicsDevice.SetVertexBuffer(_lineBuffer);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, 3);
        }

        // Draw letter labels
        graphicsDevice.SetVertexBuffer(_labelBuffer);
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, _labelVertexCount / 2);
        }

        // Restore state
        graphicsDevice.Viewport = prevViewport;
        graphicsDevice.DepthStencilState = prevDepth;
        effect.World = prevWorld;
        effect.View = prevView;
        effect.Projection = prevProjection;
        effect.VertexColorEnabled = prevVertexColor;
        effect.LightingEnabled = prevLighting;
    }

    /// <summary>
    /// Builds tiny line-segment letters (X, Y, Z) at the end of each axis.
    /// </summary>
    private static VertexPositionColor[] BuildLabels()
    {
        var verts = new List<VertexPositionColor>();
        float s = 0.09f; // letter size
        float offset = AxisLength + 0.15f;

        // "X" at end of X axis — two crossing lines
        AddLine(verts, new Vector3(offset - s, s, 0), new Vector3(offset + s, -s, 0), Color.Red);
        AddLine(verts, new Vector3(offset - s, -s, 0), new Vector3(offset + s, s, 0), Color.Red);

        // "Y" at end of Y axis — fork from top, stem down
        AddLine(verts, new Vector3(-s, offset + s, 0), new Vector3(0, offset, 0), Color.Green);
        AddLine(verts, new Vector3(s, offset + s, 0), new Vector3(0, offset, 0), Color.Green);
        AddLine(verts, new Vector3(0, offset, 0), new Vector3(0, offset - s, 0), Color.Green);

        // "Z" at end of Z axis — top line, diagonal, bottom line
        AddLine(verts, new Vector3(-s, s, offset), new Vector3(s, s, offset), Color.Blue);
        AddLine(verts, new Vector3(s, s, offset), new Vector3(-s, -s, offset), Color.Blue);
        AddLine(verts, new Vector3(-s, -s, offset), new Vector3(s, -s, offset), Color.Blue);

        return verts.ToArray();
    }

    private static void AddLine(List<VertexPositionColor> verts, Vector3 a, Vector3 b, Color color)
    {
        verts.Add(new VertexPositionColor(a, color));
        verts.Add(new VertexPositionColor(b, color));
    }

    public void Dispose()
    {
        _lineBuffer.Dispose();
        _labelBuffer.Dispose();
    }
}
