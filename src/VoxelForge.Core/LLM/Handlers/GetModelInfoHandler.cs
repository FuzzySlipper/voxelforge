using System.Text.Json;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class GetModelInfoHandler : IToolHandler
{
    public string ToolName => "get_model_info";

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Get information about the current voxel model: dimensions, voxel count, palette, and region list.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var bounds = model.GetBounds();
        var info = new
        {
            voxelCount = model.GetVoxelCount(),
            gridHint = model.GridHint,
            bounds = bounds is not null ? new { min = $"{bounds.Value.Min}", max = $"{bounds.Value.Max}" } : null,
            paletteEntries = model.Palette.Entries.Select(e => new { index = e.Key, name = e.Value.Name, color = $"({e.Value.Color.R},{e.Value.Color.G},{e.Value.Color.B})" }),
            regions = labels.Regions.Select(r => new { id = r.Key.Value, name = r.Value.Name, voxelCount = r.Value.Voxels.Count }),
            animationClips = clips.Select(c => new { name = c.Name, frameCount = c.Frames.Count, frameRate = c.FrameRate }),
        };

        return new ToolHandlerResult { Content = JsonSerializer.Serialize(info) };
    }
}
