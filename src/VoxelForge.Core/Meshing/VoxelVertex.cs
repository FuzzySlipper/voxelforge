namespace VoxelForge.Core.Meshing;

/// <summary>
/// A single vertex for voxel mesh rendering: position, normal, and color.
/// No MonoGame types — plain value type suitable for uploading to any GPU API.
/// </summary>
public readonly record struct VoxelVertex(
    float X, float Y, float Z,
    float NX, float NY, float NZ,
    byte R, byte G, byte B, byte A);
