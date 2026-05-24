using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Mcp.Services;

/// <summary>
/// Screenshot capture via headless Chromium (Playwright-free, direct CLI).
/// Launches /usr/bin/chromium in headless mode, navigates to the local viewer
/// URL with camera query params, and saves a PNG screenshot.
/// </summary>
public sealed class ChromiumViewerCaptureService : IViewerCaptureService, IDisposable
{
    private static readonly string[] ChromiumPaths =
        ["/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome"];

    private readonly string _viewerBaseUrl;
    private readonly string _capturesDir;
    private readonly ILogger<ChromiumViewerCaptureService> _logger;
    private readonly string? _chromiumPath;

    public ChromiumViewerCaptureService(
        string viewerBaseUrl,
        string capturesDir,
        ILogger<ChromiumViewerCaptureService> logger)
    {
        ArgumentNullException.ThrowIfNull(viewerBaseUrl);
        ArgumentNullException.ThrowIfNull(capturesDir);
        ArgumentNullException.ThrowIfNull(logger);

        _viewerBaseUrl = viewerBaseUrl.TrimEnd('/');
        _capturesDir = capturesDir;
        _logger = logger;

        _chromiumPath = ResolveChromiumPath();
        if (_chromiumPath is null)
        {
            _logger.LogWarning("No Chromium binary found at checked paths: {Paths}", string.Join(", ", ChromiumPaths));
        }
        else
        {
            _logger.LogInformation("Using Chromium binary: {Path}", _chromiumPath);
        }

        Directory.CreateDirectory(_capturesDir);
    }

    public Task<ViewerCaptureResult> CaptureAsync(ViewerCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CaptureSingleAsync(request, cancellationToken);
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
            var result = await CaptureSingleAsync(preset, cancellationToken);
            results.Add(result);
        }

        return new ViewerCaptureManifest
        {
            CapturesDirectory = _capturesDir,
            CapturedAtUtc = DateTime.UtcNow,
            Captures = results.AsReadOnly(),
        };
    }

    private async Task<ViewerCaptureResult> CaptureSingleAsync(ViewerCaptureRequest request, CancellationToken cancellationToken)
    {
        var label = request.Label ?? request.Preset ?? $"yaw{request.Yaw ?? 0:F2}_pitch{request.Pitch ?? 0:F2}";
        var sanitizedLabel = SanitizeFileName(label);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{sanitizedLabel}_{timestamp}.png";
        var outputPath = Path.Combine(_capturesDir, fileName);

        if (_chromiumPath is null)
        {
            _logger.LogWarning("Chromium not available; returning placeholder for {Label}", label);
            return new ViewerCaptureResult
            {
                Success = false,
                ErrorMessage = $"Chromium binary not found. Checked: {string.Join(", ", ChromiumPaths)}. Install chromium or provide a working binary at those paths.",
                ImagePath = outputPath,
                Label = label,
                Preset = request.Preset,
                Yaw = request.Yaw,
                Pitch = request.Pitch,
            };
        }

        var viewerUrl = request.ToViewerQueryString(_viewerBaseUrl + "/viewer");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _chromiumPath,
                // Use argument list to avoid shell invocation
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            // Build argument list for a headless screenshot. The virtual-time
            // budget gives the viewer time to load CDN scripts, fetch the mesh
            // snapshot, apply camera params, and draw at least one frame before
            // Chromium writes the screenshot.
            psi.ArgumentList.Add("--headless=new");
            psi.ArgumentList.Add("--no-sandbox");
            psi.ArgumentList.Add("--use-gl=swiftshader");
            psi.ArgumentList.Add("--enable-unsafe-swiftshader");
            psi.ArgumentList.Add("--ignore-gpu-blocklist");
            psi.ArgumentList.Add("--disable-dev-shm-usage");
            psi.ArgumentList.Add("--run-all-compositor-stages-before-draw");
            psi.ArgumentList.Add("--virtual-time-budget=8000");
            psi.ArgumentList.Add("--timeout=30000");
            psi.ArgumentList.Add($"--window-size={request.Width},{request.Height}");
            psi.ArgumentList.Add($"--screenshot={outputPath}");
            psi.ArgumentList.Add("--disable-extensions");
            psi.ArgumentList.Add("--mute-audio");
            psi.ArgumentList.Add(viewerUrl);

            _logger.LogInformation(
                "Launching Chromium capture: {Label} -> {OutputPath}",
                label, outputPath);

            using var process = new Process { StartInfo = psi };
            var sw = Stopwatch.StartNew();
            process.Start();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            // Read stdout/stderr in background to avoid deadlocks.
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx) when (killEx is InvalidOperationException or NotSupportedException)
                {
                    _logger.LogDebug(killEx, "Chromium process already exited or could not be killed for {Label}", label);
                }

                return new ViewerCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Chromium capture timed out after 30 seconds.",
                    ImagePath = outputPath,
                    Label = label,
                    Preset = request.Preset,
                    Yaw = request.Yaw,
                    Pitch = request.Pitch,
                };
            }
            sw.Stop();

            string stderr = string.Empty;
            string stdout = string.Empty;
            try
            {
                stderr = await stderrTask;
                stdout = await stdoutTask;
            }
            catch (OperationCanceledException)
            {
                // Process has exited or timed out; output is only diagnostic.
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Chromium exited code {ExitCode} for {Label}. stderr: {Stderr}",
                    process.ExitCode, label, stderr);

                // Chromium often exits non-zero even on success for headless screenshots
                // due to sandbox/dev-shm warnings. Check if the output file exists.
                if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
                {
                    return new ViewerCaptureResult
                    {
                        Success = false,
                        ErrorMessage = $"Chromium exited code {process.ExitCode}. Output file missing or empty. stderr: {TruncateForMessage(stderr, 200)}",
                        ImagePath = outputPath,
                        Label = label,
                        Preset = request.Preset,
                        Yaw = request.Yaw,
                        Pitch = request.Pitch,
                    };
                }
            }

            if (!File.Exists(outputPath))
            {
                return new ViewerCaptureResult
                {
                    Success = false,
                    ErrorMessage = "Output file was not created by Chromium.",
                    ImagePath = outputPath,
                    Label = label,
                    Preset = request.Preset,
                    Yaw = request.Yaw,
                    Pitch = request.Pitch,
                };
            }

            var fileInfo = new FileInfo(outputPath);
            _logger.LogInformation(
                "Captured {Label} in {ElapsedMs}ms -> {Path} ({Size} bytes)",
                label, sw.ElapsedMilliseconds, outputPath, fileInfo.Length);

            return new ViewerCaptureResult
            {
                Success = true,
                ImagePath = Path.GetFullPath(outputPath),
                Label = label,
                Preset = request.Preset,
                Yaw = request.Yaw,
                Pitch = request.Pitch,
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Capture cancelled for {Label}", label);
            return new ViewerCaptureResult
            {
                Success = false,
                ErrorMessage = "Capture was cancelled.",
                ImagePath = outputPath,
                Label = label,
                Preset = request.Preset,
                Yaw = request.Yaw,
                Pitch = request.Pitch,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to capture {Label}", label);
            return new ViewerCaptureResult
            {
                Success = false,
                ErrorMessage = $"Capture failed: {ex.Message}",
                ImagePath = outputPath,
                Label = label,
                Preset = request.Preset,
                Yaw = request.Yaw,
                Pitch = request.Pitch,
            };
        }
    }

    private static string? ResolveChromiumPath()
    {
        foreach (var path in ChromiumPaths)
        {
            if (File.Exists(path))
                return path;
        }
        return null;
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

    private static string TruncateForMessage(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxChars ? text : text[..maxChars] + "...";
    }

    public void Dispose() { }
}
