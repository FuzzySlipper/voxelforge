namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral 3D integer point, matching <c>VoxelForge.Core.Point3</c>
/// but decoupled from Core so snapshot DTOs remain in App with no dependency
/// on Core model types for external consumers.
/// </summary>
public readonly record struct Point3Snapshot(int X, int Y, int Z);