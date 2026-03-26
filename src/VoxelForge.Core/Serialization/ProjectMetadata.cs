namespace VoxelForge.Core.Serialization;

/// <summary>
/// Non-DTO metadata for an in-memory project.
/// </summary>
public sealed class ProjectMetadata
{
    public string Name { get; set; } = "Untitled";
    public string? Author { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
