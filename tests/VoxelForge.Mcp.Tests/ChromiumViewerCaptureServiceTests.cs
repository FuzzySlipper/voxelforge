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
}
