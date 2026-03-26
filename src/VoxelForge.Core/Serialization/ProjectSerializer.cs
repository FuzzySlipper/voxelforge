using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace VoxelForge.Core.Serialization;

/// <summary>
/// Serializes/deserializes VoxelForge projects to/from JSON.
/// </summary>
public sealed class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    private readonly ILoggerFactory _loggerFactory;

    public ProjectSerializer(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public string Serialize(
        VoxelModel model,
        LabelIndex labels,
        IReadOnlyList<AnimationClip> clips,
        ProjectMetadata meta)
    {
        var dto = MapToDto(model, labels, clips, meta);
        return JsonSerializer.Serialize(dto, Options);
    }

    public (VoxelModel Model, LabelIndex Labels, List<AnimationClip> Clips, ProjectMetadata Meta)
        Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<VoxelForgeProjectDto>(json, Options)
            ?? throw new InvalidOperationException("Failed to deserialize project: null result");

        if (dto.FormatVersion != 1)
            throw new NotSupportedException(
                $"Unknown format version: {dto.FormatVersion}. This version of VoxelForge supports format version 1.");

        return MapFromDto(dto);
    }

    private static VoxelForgeProjectDto MapToDto(
        VoxelModel model,
        LabelIndex labels,
        IReadOnlyList<AnimationClip> clips,
        ProjectMetadata meta)
    {
        var dto = new VoxelForgeProjectDto
        {
            FormatVersion = 1,
            Metadata = new ProjectMetadataDto
            {
                Name = meta.Name,
                Author = meta.Author,
                GridHint = model.GridHint,
                CreatedAt = meta.CreatedAt,
            },
        };

        // Palette
        foreach (var (index, mat) in model.Palette.Entries)
        {
            dto.Palette.Add(new PaletteEntryDto
            {
                Index = index,
                Name = mat.Name,
                R = mat.Color.R,
                G = mat.Color.G,
                B = mat.Color.B,
                A = mat.Color.A,
                Metadata = mat.Metadata.Count > 0 ? new Dictionary<string, string>(mat.Metadata) : null,
            });
        }

        // Voxels
        foreach (var (pos, paletteIndex) in model.Voxels)
        {
            dto.Voxels.Add(new VoxelEntryDto
            {
                X = pos.X, Y = pos.Y, Z = pos.Z,
                PaletteIndex = paletteIndex,
            });
        }

        // Regions
        foreach (var (_, regionDef) in labels.Regions)
        {
            var regionDto = new RegionDefDto
            {
                Id = regionDef.Id.Value,
                Name = regionDef.Name,
                ParentId = regionDef.ParentId?.Value,
                Properties = regionDef.Properties.Count > 0
                    ? new Dictionary<string, string>(regionDef.Properties)
                    : null,
            };

            foreach (var pos in regionDef.Voxels)
                regionDto.Voxels.Add(new VoxelPositionDto { X = pos.X, Y = pos.Y, Z = pos.Z });

            dto.Regions.Add(regionDto);
        }

        // Animation clips
        foreach (var clip in clips)
        {
            var clipDto = new AnimationClipDto
            {
                Name = clip.Name,
                FrameRate = clip.FrameRate,
            };

            foreach (var frame in clip.Frames)
            {
                var frameDto = new AnimationFrameDto { Duration = frame.Duration };

                foreach (var (pos, value) in frame.VoxelOverrides)
                {
                    frameDto.VoxelOverrides.Add(new FrameOverrideDto
                    {
                        X = pos.X, Y = pos.Y, Z = pos.Z,
                        PaletteIndex = value,
                    });
                }

                clipDto.Frames.Add(frameDto);
            }

            dto.AnimationClips.Add(clipDto);
        }

        return dto;
    }

    private (VoxelModel Model, LabelIndex Labels, List<AnimationClip> Clips, ProjectMetadata Meta)
        MapFromDto(VoxelForgeProjectDto dto)
    {
        var modelLogger = _loggerFactory.CreateLogger<VoxelModel>();
        var model = new VoxelModel(modelLogger)
        {
            GridHint = dto.Metadata.GridHint,
        };

        // Palette
        foreach (var entry in dto.Palette)
        {
            var matMetadata = entry.Metadata ?? [];
            model.Palette.Set(entry.Index, new MaterialDef
            {
                Name = entry.Name,
                Color = new RgbaColor(entry.R, entry.G, entry.B, entry.A),
                Metadata = new Dictionary<string, string>(matMetadata),
            });
        }

        // Voxels
        foreach (var v in dto.Voxels)
            model.SetVoxel(new Point3(v.X, v.Y, v.Z), v.PaletteIndex);

        // Regions
        var labelLogger = _loggerFactory.CreateLogger<LabelIndex>();
        var labels = new LabelIndex(labelLogger);

        var regionDefs = new List<RegionDef>();
        foreach (var r in dto.Regions)
        {
            var voxels = new HashSet<Point3>();
            foreach (var vp in r.Voxels)
                voxels.Add(new Point3(vp.X, vp.Y, vp.Z));

            regionDefs.Add(new RegionDef
            {
                Id = new RegionId(r.Id),
                Name = r.Name,
                ParentId = r.ParentId is not null ? new RegionId(r.ParentId) : null,
                Voxels = voxels,
                Properties = r.Properties is not null
                    ? new Dictionary<string, string>(r.Properties)
                    : [],
            });
        }
        labels.Rebuild(regionDefs);

        // Animation clips
        var clipLogger = _loggerFactory.CreateLogger<AnimationClip>();
        var clips = new List<AnimationClip>();

        foreach (var clipDto in dto.AnimationClips)
        {
            var clip = new AnimationClip(model, clipLogger)
            {
                Name = clipDto.Name,
                FrameRate = clipDto.FrameRate,
            };

            foreach (var frameDto in clipDto.Frames)
            {
                clip.AddFrame();
                int frameIdx = clip.Frames.Count - 1;

                if (frameDto.Duration.HasValue)
                    clip.Frames[frameIdx].Duration = frameDto.Duration;

                foreach (var ov in frameDto.VoxelOverrides)
                    clip.SetFrameOverride(frameIdx, new Point3(ov.X, ov.Y, ov.Z), ov.PaletteIndex);
            }

            clips.Add(clip);
        }

        var meta = new ProjectMetadata
        {
            Name = dto.Metadata.Name,
            Author = dto.Metadata.Author,
            CreatedAt = dto.Metadata.CreatedAt,
        };

        return (model, labels, clips, meta);
    }
}
