using System.Text.Json.Serialization;

namespace VoxelForge.Core.Serialization;

public sealed class VoxelForgeProjectDto
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("metadata")]
    public ProjectMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("palette")]
    public List<PaletteEntryDto> Palette { get; set; } = [];

    [JsonPropertyName("voxels")]
    public List<VoxelEntryDto> Voxels { get; set; } = [];

    [JsonPropertyName("regions")]
    public List<RegionDefDto> Regions { get; set; } = [];

    [JsonPropertyName("animationClips")]
    public List<AnimationClipDto> AnimationClips { get; set; } = [];
}

public sealed class ProjectMetadataDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("gridHint")]
    public int GridHint { get; set; } = 32;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PaletteEntryDto
{
    [JsonPropertyName("index")]
    public byte Index { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("r")]
    public byte R { get; set; }

    [JsonPropertyName("g")]
    public byte G { get; set; }

    [JsonPropertyName("b")]
    public byte B { get; set; }

    [JsonPropertyName("a")]
    public byte A { get; set; } = 255;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class VoxelEntryDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("i")]
    public byte PaletteIndex { get; set; }
}

public sealed class RegionDefDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("voxels")]
    public List<VoxelPositionDto> Voxels { get; set; } = [];

    [JsonPropertyName("properties")]
    public Dictionary<string, string>? Properties { get; set; }
}

public sealed class VoxelPositionDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }
}

public sealed class AnimationClipDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("frameRate")]
    public int FrameRate { get; set; } = 12;

    [JsonPropertyName("frames")]
    public List<AnimationFrameDto> Frames { get; set; } = [];
}

public sealed class AnimationFrameDto
{
    [JsonPropertyName("duration")]
    public float? Duration { get; set; }

    [JsonPropertyName("voxelOverrides")]
    public List<FrameOverrideDto> VoxelOverrides { get; set; } = [];
}

public sealed class FrameOverrideDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("i")]
    public byte? PaletteIndex { get; set; }
}
