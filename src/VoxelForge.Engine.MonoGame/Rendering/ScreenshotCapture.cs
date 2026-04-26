using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using VoxelForge.App;
using VoxelForge.App.Reference;
using VoxelForge.Core.Meshing;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Captures the 3D viewport to PNG bytes by rendering to a RenderTarget2D.
/// </summary>
public sealed class ScreenshotCapture : IScreenshotProvider
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly EditorState _editorState;
    private readonly OrbitalCamera _camera;
    private readonly VoxelRenderer _voxelRenderer;
    private readonly ReferenceModelRenderer _refRenderer;
    private readonly GridFloor _gridFloor;
    private readonly BasicEffect _effect;
    private readonly EditorConfigState _config;

    public ScreenshotCapture(
        GraphicsDevice graphicsDevice,
        EditorState editorState,
        OrbitalCamera camera,
        VoxelRenderer voxelRenderer,
        ReferenceModelRenderer refRenderer,
        GridFloor gridFloor,
        BasicEffect effect,
        EditorConfigState config)
    {
        _graphicsDevice = graphicsDevice;
        _editorState = editorState;
        _camera = camera;
        _voxelRenderer = voxelRenderer;
        _refRenderer = refRenderer;
        _gridFloor = gridFloor;
        _effect = effect;
        _config = config;
    }

    public byte[] CaptureViewport()
    {
        return RenderToBytes(_camera.Yaw, _camera.Pitch);
    }

    public byte[] CaptureFromAngle(float yaw, float pitch)
    {
        return RenderToBytes(yaw, pitch);
    }

    public byte[][] CaptureMultiAngle()
    {
        // Front, back, left, right, top
        (float yaw, float pitch)[] angles =
        [
            (0f, 0f),
            (MathHelper.Pi, 0f),
            (-MathHelper.PiOver2, 0f),
            (MathHelper.PiOver2, 0f),
            (0f, MathHelper.PiOver2 - 0.01f),
        ];

        var results = new byte[angles.Length][];
        for (int i = 0; i < angles.Length; i++)
            results[i] = RenderToBytes(angles[i].yaw, angles[i].pitch);

        return results;
    }

    private byte[] RenderToBytes(float yaw, float pitch)
    {
        int w = _graphicsDevice.Viewport.Width;
        int h = _graphicsDevice.Viewport.Height;

        // Minimum size for headless mode
        if (w < 64) w = 1024;
        if (h < 64) h = 768;

        using var rt = new RenderTarget2D(_graphicsDevice, w, h, false,
            SurfaceFormat.Color, DepthFormat.Depth24);

        // Save camera state
        float savedYaw = _camera.Yaw;
        float savedPitch = _camera.Pitch;

        _camera.Yaw = yaw;
        _camera.Pitch = pitch;

        // Render to target
        _graphicsDevice.SetRenderTarget(rt);
        var bg = _config.BackgroundColor;
        _graphicsDevice.Clear(new Color(bg[0], bg[1], bg[2]));
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;

        float aspect = (float)w / h;
        _effect.View = _camera.GetView();
        _effect.Projection = _camera.GetProjection(aspect);

        _effect.World = Matrix.Identity;
        _gridFloor.Draw(_graphicsDevice, _effect);
        _voxelRenderer.Draw(_editorState.ActiveModel, _camera, _effect);
        _refRenderer.Draw(_camera, _effect);

        // Restore camera
        _camera.Yaw = savedYaw;
        _camera.Pitch = savedPitch;

        _graphicsDevice.SetRenderTarget(null);

        // Encode to PNG
        using var ms = new MemoryStream();
        rt.SaveAsPng(ms, w, h);
        return ms.ToArray();
    }
}
