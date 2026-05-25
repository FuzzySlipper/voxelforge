using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Mcp.Services;

namespace VoxelForge.Mcp.Tests;

public sealed class ChromiumViewerCaptureServiceTests
{
    [Fact]
    public void TimeoutConstants_HaveExpectedValues()
    {
        Assert.Equal(20000, ChromiumViewerCaptureService.VirtualTimeBudgetMilliseconds);
        Assert.Equal(120000, ChromiumViewerCaptureService.ChromiumTimeoutMilliseconds);
        Assert.Equal(130, ChromiumViewerCaptureService.ProcessTimeoutSeconds);
    }

    [Fact]
    public void TimeoutConstants_AreInSensibleOrder()
    {
        // Virtual-time budget must be smaller than Chromium's own --timeout
        // so the page has time to finish before Chromium aborts.
        Assert.True(
            ChromiumViewerCaptureService.VirtualTimeBudgetMilliseconds < ChromiumViewerCaptureService.ChromiumTimeoutMilliseconds,
            "Virtual-time budget should be less than Chromium timeout");

        // Chromium --timeout must be smaller than the outer process kill
        // so we let Chromium finish on its own before we force-kill.
        Assert.True(
            ChromiumViewerCaptureService.ChromiumTimeoutMilliseconds < ChromiumViewerCaptureService.ProcessTimeoutSeconds * 1000,
            "Chromium timeout should be less than outer process timeout");
    }

    [Fact]
    public void BuildChromiumArgumentList_GeneratesExpectedFlags()
    {
        var request = new ViewerCaptureRequest
        {
            Width = 1024,
            Height = 768,
            Preset = "front",
        };
        var outputPath = "/tmp/capture.png";
        var viewerUrl = "http://localhost:5000/viewer?preset=front&capture=1";

        var args = new List<string>();
        ChromiumViewerCaptureService.BuildChromiumArgumentList(args, request, outputPath, viewerUrl);

        Assert.Contains("--headless=new", args);
        Assert.Contains("--no-sandbox", args);
        Assert.Contains("--use-gl=swiftshader", args);
        Assert.Contains("--enable-unsafe-swiftshader", args);
        Assert.Contains("--ignore-gpu-blocklist", args);
        Assert.Contains("--disable-dev-shm-usage", args);
        Assert.Contains("--run-all-compositor-stages-before-draw", args);
        Assert.Contains($"--virtual-time-budget={ChromiumViewerCaptureService.VirtualTimeBudgetMilliseconds}", args);
        Assert.Contains($"--timeout={ChromiumViewerCaptureService.ChromiumTimeoutMilliseconds}", args);
        Assert.Contains("--window-size=1024,768", args);
        Assert.Contains($"--screenshot={outputPath}", args);
        Assert.Contains("--disable-extensions", args);
        Assert.Contains("--mute-audio", args);
        Assert.Contains(viewerUrl, args);
    }

    [Fact]
    public void BuildChromiumArgumentList_UsesRequestDimensions()
    {
        var request = new ViewerCaptureRequest
        {
            Width = 1920,
            Height = 1080,
        };

        var args = new List<string>();
        ChromiumViewerCaptureService.BuildChromiumArgumentList(args, request, "/tmp/out.png", "http://localhost/viewer");

        Assert.Contains("--window-size=1920,1080", args);
    }

    // ── ViewerCaptureRequest.ToViewerQueryString tests ──

    [Fact]
    public void ToViewerQueryString_ProducesDistinctUrlsForDifferentPresets()
    {
        var baseUrl = "http://localhost:5000/viewer";

        var front = new ViewerCaptureRequest { Preset = "front" };
        var right = new ViewerCaptureRequest { Preset = "right" };
        var iso = new ViewerCaptureRequest { Preset = "isometric" };

        var frontUrl = front.ToViewerQueryString(baseUrl);
        var rightUrl = right.ToViewerQueryString(baseUrl);
        var isoUrl = iso.ToViewerQueryString(baseUrl);

        // Each URL must be distinct
        Assert.NotEqual(frontUrl, rightUrl);
        Assert.NotEqual(frontUrl, isoUrl);
        Assert.NotEqual(rightUrl, isoUrl);

        // Each URL must contain the correct preset name
        Assert.Contains("preset=front", frontUrl);
        Assert.Contains("preset=right", rightUrl);
        Assert.Contains("preset=isometric", isoUrl);

        // All must have capture=1 mode
        Assert.Contains("capture=1", frontUrl);
        Assert.Contains("capture=1", rightUrl);
        Assert.Contains("capture=1", isoUrl);
    }

    [Fact]
    public void ToViewerQueryString_UsesYawPitchWhenPresetNotSet()
    {
        var baseUrl = "http://localhost:5000/viewer";

        var request = new ViewerCaptureRequest
        {
            Yaw = 1.5708,
            Pitch = 0.6155,
        };

        var url = request.ToViewerQueryString(baseUrl);

        // Must include yaw/pitch params, not preset
        Assert.Contains("yaw=1.5708", url);
        Assert.Contains("pitch=0.6155", url);
        Assert.DoesNotContain("preset=", url);
        Assert.Contains("capture=1", url);
    }

    [Fact]
    public void ToViewerQueryString_IncludesDistanceWhenSet()
    {
        var baseUrl = "http://localhost:5000/viewer";

        var request = new ViewerCaptureRequest
        {
            Preset = "isometric",
            Distance = 15.0,
        };

        var url = request.ToViewerQueryString(baseUrl);

        Assert.Contains("preset=isometric", url);
        Assert.Contains("distance=15", url);
        Assert.Contains("capture=1", url);
    }

    [Fact]
    public async Task CapturePresetsAsync_PassesPresetThroughCaptureResults()
    {
        // Integration-style test: verify FakeViewerCaptureService preserves
        // preset names from requests through to results.
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-query-presets");
        try { Directory.Delete(capturesDir, recursive: true); } catch { }

        var service = new FakeViewerCaptureService(capturesDir);

        var requests = new List<ViewerCaptureRequest>
        {
            new() { Preset = "front", Label = "front" },
            new() { Preset = "right", Label = "right" },
            new() { Preset = "isometric", Label = "isometric" },
        };

        var manifest = await service.CapturePresetsAsync(requests, CancellationToken.None);

        Assert.Equal(3, manifest.Captures.Count);

        var presets = manifest.Captures.Select(c => c.Preset).ToArray();
        Assert.Contains("front", presets);
        Assert.Contains("right", presets);
        Assert.Contains("isometric", presets);

        // Results must have distinct preset labels
        var labels = manifest.Captures.Select(c => c.Label).Distinct().ToArray();
        Assert.Equal(3, labels.Length);
        Assert.All(labels, l => Assert.NotNull(l));

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }
}
