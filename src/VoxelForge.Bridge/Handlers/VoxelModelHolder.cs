using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Holds the currently loaded VoxelModel for the sidecar.
/// The sidecar loads a sample model on startup for the static
/// renderer vertical slice. Future tasks will add
/// <c>voxelforge.project.load</c> commands for dynamic model loading.
/// </summary>
public sealed class VoxelModelHolder
{
    private readonly ILogger<VoxelModelHolder> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IEventPublisher _events;

    public bool IsLoaded { get; private set; }
    public EditorDocumentState Document { get; private set; } = null!;
    public EditorSessionState Session { get; } = new();
    public UndoHistoryState UndoHistory { get; } = new(100);
    public UndoStack UndoStack { get; }
    public VoxelModel Model => Document.Model;
    public LabelIndex Labels => Document.Labels;
    public string ModelId { get; private set; } = "";
    public string? ProjectPath { get; private set; }
    public bool IsDirty { get; private set; }
    public string StatusMessage { get; private set; } = "Starting VoxelForge bridge.";

    public VoxelModelHolder(
        ILogger<VoxelModelHolder> logger,
        ILoggerFactory loggerFactory,
        IEventPublisher? events = null)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _events = events ?? new NoopEventPublisher();
        UndoStack = new UndoStack(
            UndoHistory,
            loggerFactory.CreateLogger<UndoStack>(),
            _events);
    }

    /// <summary>
    /// Load a .vforge model file. Replaces any previously loaded model.
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

        Document = new EditorDocumentState(model, labels, clips);
        ModelId = meta.Name ?? Path.GetFileNameWithoutExtension(path);
        ProjectPath = path;
        IsDirty = false;
        IsLoaded = true;
        StatusMessage = $"Loaded '{ModelId}' from {path}";

        _logger.LogInformation(
            "Loaded model '{ModelId}' with {VoxelCount} voxels, {PaletteCount} palette entries, {LabelCount} labels",
            ModelId,
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

        Document = new EditorDocumentState(model, labels);
        ModelId = "default-cube";
        ProjectPath = null;
        IsDirty = false;
        IsLoaded = true;
        StatusMessage = "Created default test cube.";

        _logger.LogInformation("Created default test cube model with {VoxelCount} voxels", model.GetVoxelCount());
    }

    public void SetProjectPath(string? path)
    {
        ProjectPath = path;
    }

    public void SetModelId(string modelId)
    {
        ArgumentNullException.ThrowIfNull(modelId);
        ModelId = modelId;
    }

    public void MarkDirty(bool isDirty)
    {
        IsDirty = isDirty;
    }

    public void SetStatus(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        StatusMessage = message;
    }

    private sealed class NoopEventPublisher : IEventPublisher
    {
        public void Publish<TEvent>(TEvent applicationEvent) where TEvent : IApplicationEvent
        {
        }

        public void Publish(IApplicationEvent applicationEvent)
        {
        }

        public void PublishAll(IReadOnlyList<IApplicationEvent> applicationEvents)
        {
        }
    }
}
