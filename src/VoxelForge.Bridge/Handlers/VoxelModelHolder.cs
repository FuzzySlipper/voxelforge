using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Workspaces;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Holds the currently loaded VoxelModel for the sidecar.
/// Hosts a <see cref="VoxelForgeWorkspaceState"/> as the authoritative shared state.
/// Existing properties delegate to <c>Workspace</c> for compatibility.
/// </summary>
public sealed class VoxelModelHolder
{
    private readonly ILogger<VoxelModelHolder> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEventDispatcher _events;

    public VoxelModelHolder(
        ILogger<VoxelModelHolder> logger,
        ILoggerFactory loggerFactory,
        IEventDispatcher? events = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _events = events ?? new ApplicationEventDispatcher();

        var model = new VoxelModel(loggerFactory.CreateLogger<VoxelModel>());
        var labels = new LabelIndex(loggerFactory.CreateLogger<LabelIndex>());
        var document = new EditorDocumentState(model, labels);
        var session = new EditorSessionState();
        var undoHistory = new UndoHistoryState(100);
        var undoStack = new UndoStack(undoHistory, loggerFactory.CreateLogger<UndoStack>(), _events);
        var referenceModels = new ReferenceModelState();
        var referenceImages = new ReferenceImageState();

        Workspace = new VoxelForgeWorkspaceState(
            document,
            session,
            undoHistory,
            undoStack,
            _events,
            referenceModels,
            referenceImages)
        {
            ModelId = "",
            CurrentModelName = "untitled",
        };
    }

    /// <summary>
    /// Shared App-layer workspace state — authoritative mutable truth.
    /// </summary>
    public VoxelForgeWorkspaceState Workspace { get; }

    public bool IsLoaded => Workspace.IsLoaded;
    public EditorDocumentState Document => Workspace.Document;
    public EditorSessionState Session => Workspace.Session;
    public UndoHistoryState UndoHistory => Workspace.UndoHistory;
    public UndoStack UndoStack => Workspace.UndoStack;
    public VoxelModel Model => Workspace.Document.Model;
    public LabelIndex Labels => Workspace.Document.Labels;
    public string ModelId
    {
        get => Workspace.ModelId;
        private set => Workspace.ModelId = value;
    }
    public string? ProjectPath
    {
        get => Workspace.ProjectPath;
        private set => Workspace.ProjectPath = value;
    }
    public bool IsDirty
    {
        get => Workspace.IsDirty;
        private set => Workspace.IsDirty = value;
    }
    public string StatusMessage
    {
        get => Workspace.StatusMessage;
        private set => Workspace.StatusMessage = value;
    }

    /// <summary>
    /// Load a .vforge model file. Replaces the workspace document state.
    /// </summary>
    public void LoadFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Model file not found: {path}");
        }

        _logger.LogInformation("Loading model from {Path}", path);

        var json = File.ReadAllText(path);
        var serializer = new ProjectSerializer(_loggerFactory);
        var (model, labels, clips, meta) = serializer.Deserialize(json);

        Workspace.Document = new EditorDocumentState(model, labels, clips);
        Workspace.ModelId = meta.Name ?? Path.GetFileNameWithoutExtension(path);
        Workspace.ProjectPath = path;
        Workspace.IsDirty = false;
        Workspace.StatusMessage = $"Loaded '{Workspace.CurrentModelName}' from {path}";
        Workspace.CurrentModelName = meta.Name ?? Path.GetFileNameWithoutExtension(path);
        Workspace.IsLoaded = true;
        Workspace.IncrementRevision();

        _logger.LogInformation(
            "Loaded model '{ModelId}' with {VoxelCount} voxels, {PaletteCount} palette entries, {LabelCount} labels",
            Workspace.ModelId,
            model.GetVoxelCount(),
            model.Palette.Count,
            labels.Regions.Count);
    }

    /// <summary>
    /// Create a default model with a simple test cube for smoke testing
    /// when no .vforge file is available.
    /// </summary>
    public void LoadDefaultCube()
    {
        var model = new VoxelModel(_loggerFactory.CreateLogger<VoxelModel>())
        {
            GridHint = 32,
        };

        // Create a small 3x3x3 cube using palette index 1
        model.Palette.Set(1, new MaterialDef
        {
            Name = "Stone",
            Color = new RgbaColor(160, 160, 160, 255),
        });
        model.Palette.Set(2, new MaterialDef
        {
            Name = "Red",
            Color = new RgbaColor(255, 0, 0, 255),
        });

        // Fill inner cube with stone
        for (int x = 0; x < 3; x++)
        for (int y = 0; y < 3; y++)
        for (int z = 0; z < 3; z++)
            model.SetVoxel(new Point3(x, y, z), (byte)1);

        // Paint the top face red
        for (int x = 0; x < 3; x++)
        for (int z = 0; z < 3; z++)
            model.SetVoxel(new Point3(x, 2, z), (byte)2);

        var labelLogger = _loggerFactory.CreateLogger<LabelIndex>();
        var labels = new LabelIndex(labelLogger);

        Workspace.Document = new EditorDocumentState(model, labels);
        Workspace.ModelId = "default-cube";
        Workspace.ProjectPath = null;
        Workspace.IsDirty = false;
        Workspace.StatusMessage = "Created default test cube.";
        Workspace.CurrentModelName = "default-cube";
        Workspace.IsLoaded = true;
        Workspace.IncrementRevision();

        _logger.LogInformation("Created default test cube model with {VoxelCount} voxels", model.GetVoxelCount());
    }

    public void SetProjectPath(string? path)
    {
        Workspace.ProjectPath = path;
    }

    public void SetModelId(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        Workspace.ModelId = modelId;
    }

    public void MarkDirty(bool isDirty)
    {
        Workspace.IsDirty = isDirty;
    }

    public void SetStatus(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Workspace.StatusMessage = message;
    }
}
