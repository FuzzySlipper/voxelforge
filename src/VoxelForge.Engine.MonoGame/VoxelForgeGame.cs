using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Myra;
using Myra.Graphics2D.UI;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Console;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Core.Screenshot;
using VoxelForge.Core.Meshing;
using VoxelForge.Engine.MonoGame.Rendering;
using VoxelForge.Engine.MonoGame.UI;
using VoxelForge.Engine.MonoGame.UI.Panels;

namespace VoxelForge.Engine.MonoGame;

public sealed class VoxelForgeGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly EditorState _editorState;
    private readonly UndoStack _undoStack;
    private readonly EditorConfigState _config;
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceImageState _referenceImageState;
    private readonly IEventDispatcher _events;
    private readonly CancellationTokenSource _cts;
    private readonly MenuCommandDispatcher? _menuDispatcher;

    private BasicEffect? _effect;
    private VoxelRenderer? _voxelRenderer;
    private ReferenceModelRenderer? _refRenderer;
    private GridFloor? _gridFloor;
    private AxisIndicator? _axisIndicator;
    private OrientationGizmo? _orientationGizmo;
    private OrbitalCamera _camera = new();
    private MeasureGrid? _measureGrid;
    private ScreenshotCapture? _screenshotCapture;
    private Desktop? _desktop;
    private EditorLayout? _editorLayout;
    private ReferenceImagePanel? _imagePanel;
    private InputHandler? _inputHandler;

    /// <summary>
    /// Available after LoadContent. Used to wire screenshot commands.
    /// </summary>
    public IScreenshotProvider? ScreenshotProvider => _screenshotCapture;

    /// <summary>
    /// Signaled when LoadContent completes and GPU resources are ready.
    /// </summary>
    public ManualResetEventSlim Ready { get; } = new(false);

    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private Point? _dragPendingStart;

    public VoxelForgeGame(EditorState editorState, UndoStack undoStack, EditorConfigState config,
        ReferenceModelState referenceModelState, ReferenceImageState referenceImageState,
        IEventDispatcher events, CancellationTokenSource cts, CommandRouter? router = null, CommandContext? context = null)
    {
        _editorState = editorState;
        _undoStack = undoStack;
        _config = config;
        _camera.MaxDistance = config.MaxZoomDistance;
        _referenceModelState = referenceModelState;
        _referenceImageState = referenceImageState;
        _events = events;
        _cts = cts;
        _menuDispatcher = router is not null && context is not null
            ? new MenuCommandDispatcher(router, context)
            : null;

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

        _voxelRenderer = new VoxelRenderer(new GreedyMesher(), GraphicsDevice);
        var voxelRendererDirtyHandler = new VoxelRendererDirtyEventHandler(_voxelRenderer);
        _events.Register<VoxelModelChangedEvent>(voxelRendererDirtyHandler);
        _events.Register<PaletteChangedEvent>(voxelRendererDirtyHandler);
        _events.Register<ProjectLoadedEvent>(voxelRendererDirtyHandler);
        _events.Register<UndoHistoryChangedEvent>(voxelRendererDirtyHandler);

        _refRenderer = new ReferenceModelRenderer(GraphicsDevice, _referenceModelState);
        _events.Register<ReferenceModelChangedEvent>(new ReferenceModelRendererDirtyEventHandler(_refRenderer));
        _gridFloor = new GridFloor(GraphicsDevice);
        _measureGrid = new MeasureGrid(GraphicsDevice);
        _axisIndicator = new AxisIndicator(GraphicsDevice);
        _orientationGizmo = new OrientationGizmo(GraphicsDevice);
        _screenshotCapture = new ScreenshotCapture(
            GraphicsDevice, _editorState, _camera, _voxelRenderer,
            _refRenderer, _gridFloor, _effect, _config);
        _inputHandler = new InputHandler(_events, new VoxelEditingService(), new RegionEditingService());

        // Myra UI
        MyraEnvironment.Game = this;
        _desktop = new Desktop();
        _editorLayout = new EditorLayout(_editorState, _undoStack, _events, _menuDispatcher, _referenceModelState);
        _imagePanel = new ReferenceImagePanel(_referenceImageState, GraphicsDevice, _events);
        _editorLayout.RightSidebar.Widgets.Add(_imagePanel.Root);
        _desktop.Root = _editorLayout.Root;
        _editorLayout.RefModelPanel?.SetDesktop(_desktop);

        // Log menu-dispatched command results to console for visibility
        if (_menuDispatcher is not null)
        {
            _menuDispatcher.CommandExecuted += result =>
            {
                var prefix = result.Success ? "[OK]" : "[FAIL]";
                System.Console.Error.WriteLine($"{prefix} {result.Message}");
            };
        }

        if (_editorLayout.MenuBar is { } menuBar)
        {
            menuBar.Initialize(_desktop, Exit);
            menuBar.OnSnapFront = () => _camera.SnapToFront();
            menuBar.OnSnapSide = () => _camera.SnapToSide();
            menuBar.OnSnapTop = () => _camera.SnapToTop();
            menuBar.OnToggleWireframe = () =>
            {
                if (_voxelRenderer is not null)
                    _voxelRenderer.WireframeEnabled = !_voxelRenderer.WireframeEnabled;
            };
        }

        _previousMouse = Mouse.GetState();
        _previousKeyboard = Keyboard.GetState();

        Ready.Set();
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
            float sx = _config.InvertOrbitX ? 1f : -1f;
            float sy = _config.InvertOrbitY ? 1f : -1f;
            _camera.Rotate(dx * sx * _config.OrbitSensitivity, dy * sy * _config.OrbitSensitivity);
        }

        // Camera zoom via scroll wheel
        _camera.MaxDistance = _config.MaxZoomDistance;
        int scrollDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (scrollDelta != 0)
            _camera.Zoom(scrollDelta * _config.ZoomSensitivity);

        // WASD camera pan (move the orbital target point)
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        float speed = _config.PanSpeed * dt;
        if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
            speed *= 3f;
        float fwd = 0, right = 0, up = 0;
        if (keyboard.IsKeyDown(Keys.W)) fwd -= speed;
        if (keyboard.IsKeyDown(Keys.S)) fwd += speed;
        if (keyboard.IsKeyDown(Keys.A)) right -= speed;
        if (keyboard.IsKeyDown(Keys.D)) right += speed;
        if (keyboard.IsKeyDown(Keys.Q)) up += speed;
        if (keyboard.IsKeyDown(Keys.E)) up -= speed;
        if (fwd != 0 || right != 0 || up != 0)
            _camera.Pan(fwd, right, up);

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

        // Content browser drag-and-drop (driven by raw mouse state, not Myra events)
        if (_editorLayout?.DragDrop is { } dd)
        {
            var mousePoint = new Point(mouse.X, mouse.Y);
            bool justPressed = mouse.LeftButton == ButtonState.Pressed
                            && _previousMouse.LeftButton == ButtonState.Released;

            if (justPressed)
            {
                // Remember click position if it started over the content tree
                _dragPendingStart = _editorLayout.ContentBrowser.IsPointOverTree(mousePoint)
                    ? mousePoint : null;
            }

            if (mouse.LeftButton == ButtonState.Pressed)
            {
                if (dd.IsDragging)
                {
                    dd.UpdateDrag(mousePoint);
                }
                else if (_dragPendingStart is { } start)
                {
                    int dx = mousePoint.X - start.X;
                    int dy = mousePoint.Y - start.Y;
                    if (dx * dx + dy * dy >= 36)
                    {
                        var path = _editorLayout.ContentBrowser.GetSelectedFilePath();
                        if (path != null)
                        {
                            dd.BeginDrag(path);
                            dd.UpdateDrag(mousePoint);
                        }
                        _dragPendingStart = null;
                    }
                }
            }
            else
            {
                if (dd.IsDragging)
                    dd.EndDrag(mousePoint);
                _dragPendingStart = null;
            }
        }

        // Escape to exit
        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();

        _editorLayout?.Refresh();

        // Sync grid floor to model's grid hint
        _gridFloor?.Resize(_editorState.ActiveModel.GridHint);

        if (_measureGrid is not null)
        {
            _measureGrid.IsVisible = _config.ShowMeasureGrid;
            if (_config.ShowMeasureGrid)
                _measureGrid.Rebuild(_config.VoxelsPerMeter);
        }

        // Tick reference model animations
        _refRenderer?.UpdateAnimations((float)gameTime.ElapsedGameTime.TotalSeconds);
        _editorLayout?.RefModelPanel?.UpdateAnimationDisplay();

        _previousMouse = mouse;
        _previousKeyboard = keyboard;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var bg = _config.BackgroundColor;
        GraphicsDevice.Clear(new Color(bg[0], bg[1], bg[2]));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        if (_effect is not null)
        {
            float aspect = GraphicsDevice.Viewport.AspectRatio;
            _effect.View = _camera.GetView();
            _effect.Projection = _camera.GetProjection(aspect);

            _effect.World = Matrix.Identity;
            _gridFloor?.Draw(GraphicsDevice, _effect);
            _measureGrid?.Draw(GraphicsDevice, _effect);
            _axisIndicator?.Draw(GraphicsDevice, _effect);
            _voxelRenderer?.Draw(_editorState.ActiveModel, _camera, _effect);
            _refRenderer?.Draw(_camera, _effect);

            _orientationGizmo?.Draw(GraphicsDevice, _effect, _camera);
        }

        _desktop?.Render();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _voxelRenderer?.Dispose();
        _refRenderer?.Dispose();
        _imagePanel?.Dispose();
        _gridFloor?.Dispose();
        _measureGrid?.Dispose();
        _axisIndicator?.Dispose();
        _orientationGizmo?.Dispose();
        _effect?.Dispose();
        base.UnloadContent();
    }
}
