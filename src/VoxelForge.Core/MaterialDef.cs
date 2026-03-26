namespace VoxelForge.Core;

/// <summary>
/// Defines a material entry in a palette — color, name, and optional metadata.
/// </summary>
public sealed class MaterialDef
{
    public required string Name { get; init; }
    public required RgbaColor Color { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
}
