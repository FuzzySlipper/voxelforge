namespace VoxelForge.Core.Screenshot;

/// <summary>
/// Captures the viewport as PNG bytes. Implemented by Engine — Core has no engine types.
/// </summary>
public interface IScreenshotProvider
{
    /// <summary>Capture the current viewport as PNG bytes.</summary>
    byte[] CaptureViewport();

    /// <summary>Capture from a specific camera angle (yaw/pitch in radians).</summary>
    byte[] CaptureFromAngle(float yaw, float pitch);

    /// <summary>Capture from 5 standard angles: front, back, left, right, top.</summary>
    byte[][] CaptureMultiAngle();
}
