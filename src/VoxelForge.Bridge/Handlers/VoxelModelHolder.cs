using Microsoft.Extensions.Logging;
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

    public bool IsLoaded { get; private set; }
    public VoxelModel Model { get; private set; } = null!;
    public LabelIndex Labels { get; private set; } = null!;
    public string ModelId { get; private set; } = "";

    public VoxelModelHolder(ILogger<VoxelModelHolder> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
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

        Model = model;
        Labels = labels;
        ModelId = meta.Name ?? Path.GetFileNameWithoutExtension(path);
        IsLoaded = true;

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

        Model = model;
        Labels = labels;
        ModelId = "default-cube";
        IsLoaded = true;

        _logger.LogInformation("Created default test cube model with {VoxelCount} voxels", model.GetVoxelCount());
    }
}