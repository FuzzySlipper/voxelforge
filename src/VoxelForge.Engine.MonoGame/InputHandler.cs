using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.App.Tools;
using VoxelForge.Core;
using VoxelForge.Engine.MonoGame.Rendering;

namespace VoxelForge.Engine.MonoGame;

/// <summary>
/// Handles mouse-to-voxel raycasting and dispatches to the active editor tool.
/// </summary>
public sealed class InputHandler
{
    private readonly IEventPublisher _events;
    private readonly VoxelEditingService _voxelEditingService;
    private readonly ToolRegistry _tools;
    private bool _leftWasDown;

    public InputHandler(
        IEventPublisher events,
        VoxelEditingService voxelEditingService,
        RegionEditingService regionEditingService)
    {
        _events = events;
        _voxelEditingService = voxelEditingService;
        _tools = new ToolRegistry(voxelEditingService, regionEditingService);
    }

    public void HandleInput(
        MouseState mouse,
        MouseState previousMouse,
        KeyboardState keyboard,
        KeyboardState previousKeyboard,
        EditorState state,
        UndoStack undo,
        OrbitalCamera camera,
        GraphicsDevice graphicsDevice)
    {
        // Tool selection via number keys.
        if (keyboard.IsKeyDown(Keys.D1) && previousKeyboard.IsKeyUp(Keys.D1))
            state.ActiveTool = EditorTool.Place;
        if (keyboard.IsKeyDown(Keys.D2) && previousKeyboard.IsKeyUp(Keys.D2))
            state.ActiveTool = EditorTool.Remove;
        if (keyboard.IsKeyDown(Keys.D3) && previousKeyboard.IsKeyUp(Keys.D3))
            state.ActiveTool = EditorTool.Paint;
        if (keyboard.IsKeyDown(Keys.D4) && previousKeyboard.IsKeyUp(Keys.D4))
            state.ActiveTool = EditorTool.Select;
        if (keyboard.IsKeyDown(Keys.D5) && previousKeyboard.IsKeyUp(Keys.D5))
            state.ActiveTool = EditorTool.Fill;
        if (keyboard.IsKeyDown(Keys.D6) && previousKeyboard.IsKeyUp(Keys.D6))
            state.ActiveTool = EditorTool.Label;

        // Delete selected voxels.
        if (keyboard.IsKeyDown(Keys.Delete) && previousKeyboard.IsKeyUp(Keys.Delete))
        {
            if (state.SelectedVoxels.Count > 0)
            {
                var positions = new List<Point3>(state.SelectedVoxels.Count);
                foreach (var position in state.SelectedVoxels)
                    positions.Add(position);

                var result = _voxelEditingService.RemoveVoxels(
                    state.Document,
                    undo,
                    _events,
                    new RemoveVoxelsRequest(positions, $"Delete {positions.Count} voxels"));

                if (result.Success)
                    state.SelectedVoxels.Clear();
            }
        }

        // Left mouse button → tool dispatch.
        bool leftDown = mouse.LeftButton == ButtonState.Pressed;
        var tool = _tools.Get(state.ActiveTool);

        if (leftDown)
        {
            var hit = CastFromScreen(mouse.X, mouse.Y, state.ActiveModel, camera, graphicsDevice);

            if (!_leftWasDown)
                tool.OnMouseDown(hit, state, undo, _events);
            else
                tool.OnMouseMove(hit, state);
        }
        else if (_leftWasDown)
        {
            var hit = CastFromScreen(mouse.X, mouse.Y, state.ActiveModel, camera, graphicsDevice);
            tool.OnMouseUp(hit, state, undo, _events);
        }

        _leftWasDown = leftDown;
    }

    private static RaycastHit? CastFromScreen(
        int screenX, int screenY,
        VoxelModel model,
        OrbitalCamera camera,
        GraphicsDevice graphicsDevice)
    {
        var viewport = graphicsDevice.Viewport;
        float aspect = viewport.AspectRatio;
        var view = camera.GetView();
        var projection = camera.GetProjection(aspect);

        // Unproject near and far points to get a world-space ray.
        var nearPoint = viewport.Unproject(
            new Vector3(screenX, screenY, 0f), projection, view, Matrix.Identity);
        var farPoint = viewport.Unproject(
            new Vector3(screenX, screenY, 1f), projection, view, Matrix.Identity);

        var direction = farPoint - nearPoint;

        return VoxelRaycaster.Cast(model,
            nearPoint.X, nearPoint.Y, nearPoint.Z,
            direction.X, direction.Y, direction.Z);
    }
}
