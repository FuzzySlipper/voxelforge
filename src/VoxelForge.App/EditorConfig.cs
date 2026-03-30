using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.App;

/// <summary>
/// Persistent editor configuration. Saved to config.json in the working directory.
/// </summary>
public sealed class EditorConfig
{
    private const string ConfigPath = "config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Invert horizontal orbit direction. When true, dragging right rotates camera right.
    /// </summary>
    public bool InvertOrbitX { get; set; }

    /// <summary>
    /// Invert vertical orbit direction.
    /// </summary>
    public bool InvertOrbitY { get; set; }

    /// <summary>
    /// Mouse sensitivity for orbit rotation.
    /// </summary>
    public float OrbitSensitivity { get; set; } = 0.005f;

    /// <summary>
    /// Scroll wheel zoom sensitivity.
    /// </summary>
    public float ZoomSensitivity { get; set; } = 0.02f;

    /// <summary>
    /// WASD camera pan speed (units per second).
    /// </summary>
    public float PanSpeed { get; set; } = 30f;

    /// <summary>
    /// Default grid hint for new models.
    /// </summary>
    public int DefaultGridHint { get; set; } = 32;

    /// <summary>
    /// Maximum undo stack depth.
    /// </summary>
    public int MaxUndoDepth { get; set; } = 100;

    /// <summary>
    /// Maximum camera zoom-out distance.
    /// </summary>
    public float MaxZoomDistance { get; set; } = 200f;

    /// <summary>
    /// Background color (R,G,B).
    /// </summary>
    public int[] BackgroundColor { get; set; } = [40, 40, 45];

    public static EditorConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new EditorConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<EditorConfig>(json, JsonOptions) ?? new EditorConfig();
        }
        catch
        {
            return new EditorConfig();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
