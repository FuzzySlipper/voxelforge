using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;
using VoxelForge.Engine.MonoGame.Rendering;
using VoxelForge.Engine.MonoGame.UI;

namespace VoxelForge.Engine.MonoGame;

public sealed class VoxelForgeGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private BasicEffect? _effect;
    private VoxelRenderer? _voxelRenderer;
    private GridFloor? _gridFloor;
    private AxisIndicator? _axisIndicator;
    private OrbitalCamera _camera = new();
    private Desktop? _desktop;
    private EditorLayout? _editorLayout;
    private EditorState? _editorState;
    private UndoStack? _undoStack;
    private InputHandler? _inputHandler;

    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;

    public VoxelForgeGame()
    {
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

        // Create a demo model
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance) { GridHint = 16 };
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(160, 160, 160) });
        model.Palette.Set(2, new MaterialDef { Name = "Grass", Color = new RgbaColor(80, 160, 60) });
        model.Palette.Set(3, new MaterialDef { Name = "Wood", Color = new RgbaColor(139, 90, 43) });

        // Simple house shape
        model.FillRegion(new Point3(0, 0, 0), new Point3(15, 0, 15), 2);
        model.FillRegion(new Point3(0, 1, 0), new Point3(15, 6, 0), 1);
        model.FillRegion(new Point3(0, 1, 15), new Point3(15, 6, 15), 1);
        model.FillRegion(new Point3(0, 1, 0), new Point3(0, 6, 15), 1);
        model.FillRegion(new Point3(15, 1, 0), new Point3(15, 6, 15), 1);

        for (int i = 0; i <= 8; i++)
        {
            model.FillRegion(
                new Point3(i, 7 + i, i),
                new Point3(15 - i, 7 + i, 15 - i), 3);
        }

        // Center camera on model
        var bounds = model.GetBounds();
        if (bounds is not null)
        {
            var center = new Vector3(
                (bounds.Value.Min.X + bounds.Value.Max.X) / 2f,
                (bounds.Value.Min.Y + bounds.Value.Max.Y) / 2f,
                (bounds.Value.Min.Z + bounds.Value.Max.Z) / 2f);
            _camera.Target = center;
        }

        // Editor state
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        _editorState = new EditorState(model, labels, NullLogger<EditorState>.Instance);
        _undoStack = new UndoStack(100, NullLogger<UndoStack>.Instance);
        _undoStack.StateChanged += () =>
        {
            _voxelRenderer?.MarkDirty();
            _editorState.NotifyModelChanged();
        };

        // Rendering
        _voxelRenderer = new VoxelRenderer(new GreedyMesher(), GraphicsDevice);
        _gridFloor = new GridFloor(GraphicsDevice);
        _axisIndicator = new AxisIndicator(GraphicsDevice);

        // Input handler
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
            _undoStack?.Undo();
        if (ctrl && keyboard.IsKeyDown(Keys.Y) && _previousKeyboard.IsKeyUp(Keys.Y))
            _undoStack?.Redo();

        // Tool input (only when not right-dragging camera)
        if (mouse.RightButton != ButtonState.Pressed &&
            _editorState is not null && _undoStack is not null)
        {
            _inputHandler?.HandleInput(mouse, _previousMouse, keyboard, _previousKeyboard,
                _editorState, _undoStack, _camera, GraphicsDevice);
        }

        // Escape to exit
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();

        // Refresh properties panel each frame
        _editorLayout?.Refresh();

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(40, 40, 45));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        if (_effect is not null && _editorState is not null)
        {
            float aspect = GraphicsDevice.Viewport.AspectRatio;
            _effect.View = _camera.GetView();
            _effect.Projection = _camera.GetProjection(aspect);

            _effect.World = Matrix.Identity;
            _gridFloor?.Draw(GraphicsDevice, _effect);
            _axisIndicator?.Draw(GraphicsDevice, _effect);
            _voxelRenderer?.Draw(_editorState.ActiveModel, _camera, _effect);
        }

        // Render Myra UI on top
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
