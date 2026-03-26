using System.Text.Json;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class DescribeModelHandler : IToolHandler
{
    public string ToolName => "describe_model";

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Get a text description of the voxel model for understanding its contents.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var bounds = model.GetBounds();
        var lines = new List<string>
        {
            $"Voxel model with {model.GetVoxelCount()} voxels, grid hint {model.GridHint}.",
        };

        if (bounds is not null)
            lines.Add($"Bounds: ({bounds.Value.Min.X},{bounds.Value.Min.Y},{bounds.Value.Min.Z}) to ({bounds.Value.Max.X},{bounds.Value.Max.Y},{bounds.Value.Max.Z}).");

        // Palette summary
        if (model.Palette.Count > 0)
        {
            lines.Add($"Palette: {model.Palette.Count} colors.");
            foreach (var (idx, mat) in model.Palette.Entries)
                lines.Add($"  [{idx}] {mat.Name} ({mat.Color.R},{mat.Color.G},{mat.Color.B})");
        }

        // Region summary
        if (labels.Regions.Count > 0)
        {
            lines.Add($"Regions: {labels.Regions.Count}.");
            foreach (var (id, def) in labels.Regions)
                lines.Add($"  {def.Name}: {def.Voxels.Count} voxels");
        }

        // Material distribution
        var distribution = new Dictionary<byte, int>();
        foreach (var (_, idx) in model.Voxels)
        {
            distribution.TryGetValue(idx, out int count);
            distribution[idx] = count + 1;
        }

        if (distribution.Count > 0)
        {
            lines.Add("Material distribution:");
            foreach (var (idx, count) in distribution.OrderByDescending(kv => kv.Value))
            {
                var name = model.Palette.Get(idx)?.Name ?? $"index_{idx}";
                lines.Add($"  {name}: {count} voxels ({100.0 * count / model.GetVoxelCount():F1}%)");
            }
        }

        return new ToolHandlerResult { Content = string.Join("\n", lines) };
    }
}
