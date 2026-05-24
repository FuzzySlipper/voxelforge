namespace VoxelForge.Mcp.Services;

/// <summary>
/// Service boundary for capturing screenshots of the JS/WebGL viewer.
/// </summary>
public interface IViewerCaptureService
{
    /// <summary>
    /// Capture a single view from the JS viewer with optional camera parameters.
    /// </summary>
    /// <param name="request">Capture parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Capture result with artifact paths.</returns>
    Task<ViewerCaptureResult> CaptureAsync(ViewerCaptureRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Capture multiple standard preset views in a single batch.
    /// </summary>
    /// <param name="presets">List of preset names or yaw/pitch tuples.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined capture manifest.</returns>
    Task<ViewerCaptureManifest> CapturePresetsAsync(IReadOnlyList<ViewerCaptureRequest> presets, CancellationToken cancellationToken);
}

/// <summary>
/// Parameters for a single view capture.
/// </summary>
public sealed class ViewerCaptureRequest
{
    /// <summary>Camera yaw in radians (0=front, PI/2=right, PI=back).</summary>
    public double? Yaw { get; init; }

    /// <summary>Camera pitch in radians (0=level, PI/2=top-down).</summary>
    public double? Pitch { get; init; }

    /// <summary>Named preset (front, right, top, isometric, back). Overrides yaw/pitch when set.</summary>
    public string? Preset { get; init; }

    /// <summary>Camera distance from model center. Auto-calculated if null.</summary>
    public double? Distance { get; init; }

    /// <summary>Width in pixels (default: 960).</summary>
    public int Width { get; init; } = 960;

    /// <summary>Height in pixels (default: 720).</summary>
    public int Height { get; init; } = 720;

    /// <summary>Optional label for this capture in the manifest.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Build the viewer URL query string for this request.
    /// </summary>
    public string ToViewerQueryString(string baseUrl)
    {
        var query = new System.Text.StringBuilder(baseUrl);
        query.Append('?');

        if (Preset is { } p)
        {
            query.Append($"preset={Uri.EscapeDataString(p)}");
        }
        else
        {
            query.Append($"yaw={Yaw ?? 0}");
            query.Append($"&pitch={Pitch ?? 0}");
        }

        if (Distance.HasValue)
            query.Append($"&distance={Distance.Value}");

        return query.ToString();
    }
}

/// <summary>
/// Result of a single view capture.
/// </summary>
public sealed class ViewerCaptureResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ImagePath { get; init; }
    public string? Label { get; init; }
    public string? Preset { get; init; }
    public double? Yaw { get; init; }
    public double? Pitch { get; init; }
}

/// <summary>
/// Combined manifest for one or more capture requests.
/// </summary>
public sealed class ViewerCaptureManifest
{
    public required string CapturesDirectory { get; init; }
    public required DateTime CapturedAtUtc { get; init; }
    public required IReadOnlyList<ViewerCaptureResult> Captures { get; init; }

    /// <summary>
    /// Serialize to a compact JSON string suitable for tool response messages.
    /// </summary>
    public string ToJson()
    {
        var entries = Captures.Select(c => new
        {
            label = c.Label ?? c.Preset ?? "view",
            success = c.Success,
            image_path = c.ImagePath,
            preset = c.Preset,
            yaw = c.Yaw,
            pitch = c.Pitch,
            error = c.ErrorMessage,
        });

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            captures_directory = CapturesDirectory,
            captured_at_utc = CapturedAtUtc,
            capture_count = Captures.Count,
            successful_count = Captures.Count(c => c.Success),
            captures = entries,
        }, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        });
    }
}
