using Microsoft.Xna.Framework;

namespace VoxelForge.Engine.MonoGame.Rendering;

/// <summary>
/// Orbits around a target point. Controlled by yaw/pitch/distance.
/// </summary>
public sealed class OrbitalCamera
{
    public float Yaw { get; set; }
    public float Pitch { get; set; } = -0.4f;
    public float Distance { get; set; } = 50f;
    public Vector3 Target { get; set; } = Vector3.Zero;

    public float MinDistance { get; set; } = 5f;
    public float MaxDistance { get; set; } = 200f;
    public float MinPitch { get; set; } = -MathHelper.PiOver2 + 0.01f;
    public float MaxPitch { get; set; } = MathHelper.PiOver2 - 0.01f;

    public Matrix GetView()
    {
        float cosP = MathF.Cos(Pitch);
        float sinP = MathF.Sin(Pitch);
        float cosY = MathF.Cos(Yaw);
        float sinY = MathF.Sin(Yaw);

        var offset = new Vector3(
            cosP * sinY * Distance,
            sinP * Distance,
            cosP * cosY * Distance);

        var eye = Target + offset;
        return Matrix.CreateLookAt(eye, Target, Vector3.Up);
    }

    public Matrix GetProjection(float aspectRatio)
    {
        return Matrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4, aspectRatio, 0.1f, 1000f);
    }

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = MathHelper.Clamp(Pitch + deltaPitch, MinPitch, MaxPitch);
    }

    public void Zoom(float delta)
    {
        Distance = MathHelper.Clamp(Distance - delta, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Moves the target point relative to the camera's current orientation.
    /// Forward/back is along the camera's XZ look direction, strafe is perpendicular.
    /// </summary>
    public void Pan(float forward, float right, float up)
    {
        // Camera's forward direction projected onto XZ plane
        var fwd = new Vector3(MathF.Sin(Yaw), 0, MathF.Cos(Yaw));
        // Right is perpendicular to forward on the XZ plane
        var rt = new Vector3(fwd.Z, 0, -fwd.X);

        Target += fwd * forward + rt * right + Vector3.Up * up;
    }

    public void SnapToFront() { Yaw = 0; Pitch = 0; }
    public void SnapToSide() { Yaw = MathHelper.PiOver2; Pitch = 0; }
    public void SnapToTop() { Yaw = 0; Pitch = MathHelper.PiOver2 - 0.01f; }
}
