namespace VoxelForge.Mcp.Services;

/// <summary>
/// Fake capture service that creates lightweight placeholder PNG files.
/// Used in unit tests to verify argument validation, manifest shape, and
/// tool invocation logic without launching Chromium.
/// </summary>
public sealed class FakeViewerCaptureService : IViewerCaptureService
{
    private readonly string _capturesDir;
    private bool _simulateFailure;

    public FakeViewerCaptureService(string capturesDir, bool simulateFailure = false)
    {
        _capturesDir = capturesDir;
        _simulateFailure = simulateFailure;
        Directory.CreateDirectory(_capturesDir);
    }

    /// <summary>
    /// When set to true, all capture calls return failure results.
    /// </summary>
    public bool SimulateFailure
    {
        get => _simulateFailure;
        set => _simulateFailure = value;
    }

    public Task<ViewerCaptureResult> CaptureAsync(ViewerCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = CreatePlaceholder(request);
        return Task.FromResult(result);
    }

    public async Task<ViewerCaptureManifest> CapturePresetsAsync(
        IReadOnlyList<ViewerCaptureRequest> presets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var results = new List<ViewerCaptureResult>(presets.Count);
        foreach (var preset in presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(CreatePlaceholder(preset));
        }

        return new ViewerCaptureManifest
        {
            CapturesDirectory = _capturesDir,
            CapturedAtUtc = DateTime.UtcNow,
            Captures = results.AsReadOnly(),
        };
    }

    private ViewerCaptureResult CreatePlaceholder(ViewerCaptureRequest request)
    {
        var label = request.Label ?? request.Preset ?? $"yaw{request.Yaw ?? 0:F2}_pitch{request.Pitch ?? 0:F2}";
        var sanitized = SanitizeFileName(label);
        var fileName = $"{sanitized}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
        var path = Path.Combine(_capturesDir, fileName);

        if (_simulateFailure)
        {
            return new ViewerCaptureResult
            {
                Success = false,
                ErrorMessage = "Simulated capture failure.",
                ImagePath = path,
                Label = label,
                Preset = request.Preset,
                Yaw = request.Yaw,
                Pitch = request.Pitch,
            };
        }

        // Write a minimal valid PNG (1x1 pixel) so the file exists and is non-empty
        // Minimal PNG: 8-byte signature + minimal IHDR + IDAT + IEND
        WriteMinimalPng(path);

        return new ViewerCaptureResult
        {
            Success = true,
            ImagePath = Path.GetFullPath(path),
            Label = label,
            Preset = request.Preset,
            Yaw = request.Yaw,
            Pitch = request.Pitch,
        };
    }

    private static void WriteMinimalPng(string path)
    {
        // Minimal 1x1 white pixel PNG
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, // IHDR chunk length: 13
            0x49, 0x48, 0x44, 0x52, // "IHDR"
            0x00, 0x00, 0x00, 0x01, // width: 1
            0x00, 0x00, 0x00, 0x01, // height: 1
            0x08,                   // bit depth: 8
            0x02,                   // color type: RGB
            0x00,                   // compression
            0x00,                   // filter
            0x00,                   // interlace
            0x31, 0xAE, 0xC4, 0x07, // CRC of IHDR
            0x00, 0x00, 0x00, 0x0C, // IDAT chunk length: 12
            0x49, 0x44, 0x41, 0x54, // "IDAT"
            0x78, 0x01, 0x63, 0x60, 0x00, 0x00, 0x00, 0x02, 0x00, 0x01, 0xE7, 0x27, 0xD7, // compressed data
            0x00, 0x00, 0x00, 0x00, // IEND chunk length: 0
            0x49, 0x45, 0x4E, 0x44, // "IEND"
            0xAE, 0x42, 0x60, 0x82, // CRC of IEND
        };
        File.WriteAllBytes(path, png);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            sanitized[i] = invalid.Contains(name[i]) ? '_' : name[i];
        }
        return new string(sanitized).TrimEnd('.');
    }
}
