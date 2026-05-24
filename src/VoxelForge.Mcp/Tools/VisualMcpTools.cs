using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoxelForge.Mcp.Services;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Base class for visual MCP tools that capture screenshots via the JS viewer.
/// </summary>
public abstract class VisualMcpToolBase : IVoxelForgeMcpTool
{
    private readonly JsonElement _inputSchema;
    private readonly IViewerCaptureService _captureService;
    private readonly ILogger _logger;
    private readonly string _toolName;

    protected VisualMcpToolBase(
        string name,
        string description,
        JsonElement inputSchema,
        IViewerCaptureService captureService,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(logger);

        Name = name;
        Description = description;
        _inputSchema = inputSchema;
        _captureService = captureService;
        _logger = logger;
        _toolName = name;
    }

    public string Name { get; }
    public string Description { get; }
    public JsonElement InputSchema => _inputSchema;
    public bool IsReadOnly => true;

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var request = BuildRequest(arguments);
            var result = _captureService.CaptureAsync(request, cancellationToken)
                .GetAwaiter().GetResult();

            if (result.Success)
            {
                var manifest = BuildSingleManifest(result);
                return new McpToolInvocationResult
                {
                    Success = true,
                    Message = manifest,
                };
            }

            return new McpToolInvocationResult
            {
                Success = false,
                Message = $"Visual capture failed: {result.ErrorMessage}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error invoking visual tool '{ToolName}'", _toolName);
            return new McpToolInvocationResult
            {
                Success = false,
                Message = $"Error capturing view: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Build a capture request from the tool arguments.
    /// </summary>
    protected abstract ViewerCaptureRequest BuildRequest(JsonElement arguments);

    private string BuildSingleManifest(ViewerCaptureResult result)
    {
        var manifest = new
        {
            captures = new[]
            {
                new
                {
                    label = result.Label ?? "view",
                    success = result.Success,
                    image_path = result.ImagePath,
                    preset = result.Preset,
                    yaw = result.Yaw,
                    pitch = result.Pitch,
                },
            },
            capture_count = 1,
            successful_count = result.Success ? 1 : 0,
            captured_at_utc = DateTime.UtcNow,
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
    }
}

/// <summary>
/// ViewModelMcpTool — captures the current model from a default isometric angle.
/// </summary>
public sealed class ViewModelMcpTool : VisualMcpToolBase
{
    public ViewModelMcpTool(IViewerCaptureService captureService, ILogger<ViewModelMcpTool> logger)
        : base(
            "view_model",
            "Capture a screenshot of the current voxel model viewport from an isometric angle.",
            McpJsonSchemas.Parse("""{"type":"object","properties":{}}"""),
            captureService,
            logger)
    {
    }

    protected override ViewerCaptureRequest BuildRequest(JsonElement arguments)
    {
        return new ViewerCaptureRequest
        {
            Preset = "isometric",
            Label = "model-view",
            Width = 1280,
            Height = 960,
        };
    }
}

public sealed class ViewModelServerTool : VoxelForgeMcpServerTool
{
    public ViewModelServerTool(ViewModelMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// ViewFromAngleMcpTool — captures from explicit yaw/pitch camera angles.
/// </summary>
public sealed class ViewFromAngleMcpTool : VisualMcpToolBase
{
    public ViewFromAngleMcpTool(IViewerCaptureService captureService, ILogger<ViewFromAngleMcpTool> logger)
        : base(
            "view_from_angle",
            "Capture a screenshot from a specific camera angle. Provide yaw and pitch in radians, or use the preset parameter with named presets: front, right, back, top, isometric.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "yaw": { "type": "number", "description": "Camera yaw in radians (0=front, pi/2=right, pi=back)" },
                    "pitch": { "type": "number", "description": "Camera pitch in radians (0=level, pi/2=top-down)" },
                    "preset": { "type": "string", "description": "Named camera preset: front, right, back, top, isometric. Overrides yaw/pitch when set." },
                    "distance": { "type": "number", "description": "Camera distance from model center (optional, auto-calculated)" },
                    "width": { "type": "integer", "description": "Capture width in pixels (default: 960)" },
                    "height": { "type": "integer", "description": "Capture height in pixels (default: 720)" }
                },
                "required": []
            }
            """),
            captureService,
            logger)
    {
    }

    protected override ViewerCaptureRequest BuildRequest(JsonElement arguments)
    {
        double? yaw = null, pitch = null;
        string? preset = null;
        double? distance = null;
        int width = 960, height = 720;

        if (arguments.TryGetProperty("yaw", out var yawEl) && yawEl.ValueKind == JsonValueKind.Number)
            yaw = yawEl.GetDouble();

        if (arguments.TryGetProperty("pitch", out var pitchEl) && pitchEl.ValueKind == JsonValueKind.Number)
            pitch = pitchEl.GetDouble();

        if (arguments.TryGetProperty("preset", out var presetEl) && presetEl.ValueKind == JsonValueKind.String)
            preset = presetEl.GetString();

        if (arguments.TryGetProperty("distance", out var distEl) && distEl.ValueKind == JsonValueKind.Number)
            distance = distEl.GetDouble();

        if (arguments.TryGetProperty("width", out var wEl) && wEl.ValueKind == JsonValueKind.Number)
            width = Math.Clamp(wEl.GetInt32(), 320, 3840);

        if (arguments.TryGetProperty("height", out var hEl) && hEl.ValueKind == JsonValueKind.Number)
            height = Math.Clamp(hEl.GetInt32(), 240, 2160);

        return new ViewerCaptureRequest
        {
            Yaw = yaw,
            Pitch = pitch,
            Preset = preset,
            Distance = distance,
            Width = width,
            Height = height,
            Label = preset ?? (yaw.HasValue ? $"yaw{yaw:F2}" : null),
        };
    }
}

public sealed class ViewFromAngleServerTool : VoxelForgeMcpServerTool
{
    public ViewFromAngleServerTool(ViewFromAngleMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// CaptureReferenceViewsMcpTool — captures multiple standard views in a single batch.
/// </summary>
public sealed class CaptureReferenceViewsMcpTool : IVoxelForgeMcpTool
{
    private static readonly JsonElement _inputSchema = McpJsonSchemas.Parse("""
    {
        "type": "object",
        "properties": {
            "presets": {
                "type": "array",
                "items": { "type": "string" },
                "description": "List of named presets to capture. Default: [\"front\", \"right\", \"top\", \"isometric\"]. Options: front, right, back, top, isometric."
            },
            "width": { "type": "integer", "description": "Capture width in pixels (default: 960)" },
            "height": { "type": "integer", "description": "Capture height in pixels (default: 720)" },
            "include_reference": { "type": "boolean", "description": "Include reference model overlays if available (future use; always true if reference loaded)" },
            "include_grid": { "type": "boolean", "description": "Show grid overlay (always enabled in viewer, param reserved for future)" }
        },
        "required": []
    }
    """);

    private readonly IViewerCaptureService _captureService;
    private readonly ILogger<CaptureReferenceViewsMcpTool> _logger;

    private static readonly string[] DefaultPresets = ["front", "right", "top", "isometric"];

    public CaptureReferenceViewsMcpTool(IViewerCaptureService captureService, ILogger<CaptureReferenceViewsMcpTool> logger)
    {
        ArgumentNullException.ThrowIfNull(captureService);
        ArgumentNullException.ThrowIfNull(logger);
        _captureService = captureService;
        _logger = logger;
    }

    public string Name => "capture_reference_views";
    public string Description => "Capture the current model from multiple standard camera angles in a single batch. Returns a manifest with image paths for each view.";
    public JsonElement InputSchema => _inputSchema;
    public bool IsReadOnly => true;

    public McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var presets = ParsePresets(arguments);
            int width = 960, height = 720;

            if (arguments.TryGetProperty("width", out var wEl) && wEl.ValueKind == JsonValueKind.Number)
                width = Math.Clamp(wEl.GetInt32(), 320, 3840);

            if (arguments.TryGetProperty("height", out var hEl) && hEl.ValueKind == JsonValueKind.Number)
                height = Math.Clamp(hEl.GetInt32(), 240, 2160);

            var requests = presets.Select(p => new ViewerCaptureRequest
            {
                Preset = p,
                Label = p,
                Width = width,
                Height = height,
            }).ToList();

            var manifest = _captureService.CapturePresetsAsync(requests, cancellationToken)
                .GetAwaiter().GetResult();

            return new McpToolInvocationResult
            {
                Success = manifest.Captures.Any(c => c.Success),
                Message = manifest.ToJson(),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error invoking capture_reference_views");
            return new McpToolInvocationResult
            {
                Success = false,
                Message = $"Error capturing reference views: {ex.Message}",
            };
        }
    }

    private static string[] ParsePresets(JsonElement arguments)
    {
        if (arguments.TryGetProperty("presets", out var presetsEl) && presetsEl.ValueKind == JsonValueKind.Array)
        {
            var items = new List<string>();
            foreach (var item in presetsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString()!;
                    if (IsValidPreset(s))
                        items.Add(s);
                }
            }
            return items.Count > 0 ? [..items] : DefaultPresets;
        }

        return DefaultPresets;
    }

    private static bool IsValidPreset(string preset)
    {
        return preset is "front" or "right" or "back" or "top" or "isometric";
    }
}

public sealed class CaptureReferenceViewsServerTool : VoxelForgeMcpServerTool
{
    public CaptureReferenceViewsServerTool(CaptureReferenceViewsMcpTool tool)
        : base(tool)
    {
    }
}

// Keep the old unavailable tools for now as fallback/legacy. They are no longer registered by default.
// The CompareReferenceMcpTool remains as an unavailable stub for reference but is not wired up.
