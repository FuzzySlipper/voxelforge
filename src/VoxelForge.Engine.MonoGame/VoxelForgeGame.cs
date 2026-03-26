using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.Core.Meshing;
using VoxelForge.Engine.MonoGame.Rendering;
using VoxelForge.Engine.MonoGame.UI;

namespace VoxelForge.Engine.MonoGame;

public sealed class VoxelForgeGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EditorState _editorState;
    private readonly UndoStack _undoStack;
    private readonly CancellationTokenSource _cts;

    private BasicEffect? _effect;
    private VoxelRenderer? _voxelRenderer;
    private GridFloor? _gridFloor;
    private AxisIndicator? _axisIndicator;
    private OrbitalCamera _camera = new();
    private Desktop? _desktop;
    private EditorLayout? _editorLayout;
    private InputHandler? _inputHandler;

    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    public VoxelForgeGame(EditorState editorState, UndoStack undoStack, CancellationTokenSource cts)
    {
        _editorState = editorState;
        _undoStack = undoStack;
        _cts = cts;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            PreferMultiSampling = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "VoxelForge";
    }

    protected override void Initialize()
    {
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _effect = new BasicEffect(GraphicsDevice);

        // Center camera on model
        var bounds = _editorState.ActiveModel.GetBounds();
        if (bounds is not null)
        {
            var center = new Vector3(
                (bounds.Value.Min.X + bounds.Value.Max.X) / 2f,
                (bounds.Value.Min.Y + bounds.Value.Max.Y) / 2f,
                (bounds.Value.Min.Z + bounds.Value.Max.Z) / 2f);
            _camera.Target = center;
        }

        // Wire undo stack and model changes to renderer
        _voxelRenderer = new VoxelRenderer(new GreedyMesher(), GraphicsDevice);
        _undoStack.StateChanged += () => _voxelRenderer?.MarkDirty();
        _editorState.ModelChanged += () => _voxelRenderer?.MarkDirty();

        _gridFloor = new GridFloor(GraphicsDevice);
        _axisIndicator = new AxisIndicator(GraphicsDevice);
        _inputHandler = new InputHandler();

        // Myra UI
        MyraEnvironment.Game = this;
        _desktop = new Desktop();
        _editorLayout = new EditorLayout(_editorState);
        _desktop.Root = _editorLayout.Root;

        _previousMouse = Mouse.GetState();
        _previousKeyboard = Keyboard.GetState();
    }

    protected override void Update(GameTime gameTime)
    {
        // Check if console requested exit
        if (_cts.IsCancellationRequested)
        {
            Exit();
            return;
        }

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        // Camera rotation via right mouse drag
        if (mouse.RightButton == ButtonState.Pressed)
        {
            int dx = mouse.X - _previousMouse.X;
            int dy = mouse.Y - _previousMouse.Y;
            _camera.Rotate(dx * -0.005f, dy * -0.005f);
        }

        // Camera zoom via scroll wheel
        int scrollDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (scrollDelta != 0)
            _camera.Zoom(scrollDelta * 0.02f);

        // Axis-aligned view snaps
        if (keyboard.IsKeyDown(Keys.F1) && _previousKeyboard.IsKeyUp(Keys.F1))
            _camera.SnapToFront();
        if (keyboard.IsKeyDown(Keys.F2) && _previousKeyboard.IsKeyUp(Keys.F2))
            _camera.SnapToSide();
        if (keyboard.IsKeyDown(Keys.F3) && _previousKeyboard.IsKeyUp(Keys.F3))
            _camera.SnapToTop();

        // Wireframe toggle
        if (keyboard.IsKeyDown(Keys.F4) && _previousKeyboard.IsKeyUp(Keys.F4) && _voxelRenderer is not null)
            _voxelRenderer.WireframeEnabled = !_voxelRenderer.WireframeEnabled;

        // Undo/Redo
        bool ctrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        if (ctrl && keyboard.IsKeyDown(Keys.Z) && _previousKeyboard.IsKeyUp(Keys.Z))
            _undoStack.Undo();
        if (ctrl && keyboard.IsKeyDown(Keys.Y) && _previousKeyboard.IsKeyUp(Keys.Y))
            _undoStack.Redo();

        // Tool input (only when not right-dragging camera)
        if (mouse.RightButton != ButtonState.Pressed)
        {
            _inputHandler?.HandleInput(mouse, _previousMouse, keyboard, _previousKeyboard,
                _editorState, _undoStack, _camera, GraphicsDevice);
        }

        // Escape to exit
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();

        _editorLayout?.Refresh();

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(40, 40, 45));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        if (_effect is not null)
        {
            float aspect = GraphicsDevice.Viewport.AspectRatio;
            _effect.View = _camera.GetView();
            _effect.Projection = _camera.GetProjection(aspect);

            _effect.World = Matrix.Identity;
            _gridFloor?.Draw(GraphicsDevice, _effect);
            _axisIndicator?.Draw(GraphicsDevice, _effect);
            _voxelRenderer?.Draw(_editorState.ActiveModel, _camera, _effect);
        }

        _desktop?.Render();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _voxelRenderer?.Dispose();
        _gridFloor?.Dispose();
        _axisIndicator?.Dispose();
        _effect?.Dispose();
        base.UnloadContent();
    }
}
