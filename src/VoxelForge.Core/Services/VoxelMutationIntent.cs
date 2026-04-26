namespace VoxelForge.Core.Services;

/// <summary>
/// Parsed request for setting one voxel to a palette index. The palette value is
/// intentionally an int so validation happens before narrowing to byte.
/// </summary>
public readonly record struct VoxelAssignmentRequest(Point3 Position, int PaletteIndex);

/// <summary>
/// One requested voxel mutation. A null palette index means remove/air.
/// </summary>
public readonly record struct VoxelAssignment(Point3 Position, byte? PaletteIndex);

/// <summary>
/// Immutable intent describing voxel mutations requested by an adapter or LLM tool.
/// Application services decide how to apply the intent through undoable operations.
/// </summary>
public sealed class VoxelMutationIntent
{
    public required IReadOnlyList<VoxelAssignment> Assignments { get; init; }
    public required string Description { get; init; }
}

public sealed class VoxelMutationIntentBuildResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public VoxelMutationIntent? Intent { get; init; }
}
