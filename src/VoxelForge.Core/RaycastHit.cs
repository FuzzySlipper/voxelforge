namespace VoxelForge.Core;

/// <summary>
/// Result of a voxel raycast — which voxel was hit, which face, and the distance.
/// </summary>
public readonly record struct RaycastHit(Point3 VoxelPos, Point3 FaceNormal, float Distance);
