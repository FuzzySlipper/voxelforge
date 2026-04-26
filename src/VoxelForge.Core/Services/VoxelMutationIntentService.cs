namespace VoxelForge.Core.Services;

/// <summary>
/// Builds validated voxel mutation intents without owning or mutating model state.
/// </summary>
public sealed class VoxelMutationIntentService
{
    public VoxelMutationIntentBuildResult BuildSetIntent(IReadOnlyList<VoxelAssignmentRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var assignments = new List<VoxelAssignment>(requests.Count);
        for (int i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            if (request.PaletteIndex < 1 || request.PaletteIndex > 255)
            {
                return new VoxelMutationIntentBuildResult
                {
                    Success = false,
                    Message = $"Invalid palette index {request.PaletteIndex} at {request.Position}. Expected 1-255.",
                };
            }

            assignments.Add(new VoxelAssignment(request.Position, (byte)request.PaletteIndex));
        }

        string message = $"Set {assignments.Count} voxel(s).";
        return new VoxelMutationIntentBuildResult
        {
            Success = true,
            Message = message,
            Intent = new VoxelMutationIntent
            {
                Assignments = assignments,
                Description = message,
            },
        };
    }

    public VoxelMutationIntentBuildResult BuildRemoveIntent(IReadOnlyList<Point3> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var assignments = new List<VoxelAssignment>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
            assignments.Add(new VoxelAssignment(positions[i], null));

        string message = $"Removed {assignments.Count} voxel(s).";
        return new VoxelMutationIntentBuildResult
        {
            Success = true,
            Message = message,
            Intent = new VoxelMutationIntent
            {
                Assignments = assignments,
                Description = message,
            },
        };
    }
}
