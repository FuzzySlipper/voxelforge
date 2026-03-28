using System.Text.Json;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.Core.LLM.Handlers;

/// <summary>
/// Captures multi-angle screenshots for comparing the voxel model against a reference.
/// Returns 5 views (front, back, left, right, top) as base64 PNGs.
/// </summary>
public sealed class CompareReferenceHandler : IToolHandler
{
    private readonly Func<IScreenshotProvider?> _providerFactory;

    public string ToolName => "compare_reference";

    public CompareReferenceHandler(Func<IScreenshotProvider?> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Capture the voxel model from 5 standard angles (front, back, left, right, top). Returns base64 PNGs for visual comparison with a reference model. Load a reference model first with refload.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var provider = _providerFactory();
        if (provider is null)
            return new ToolHandlerResult { Content = "Screenshot provider not available.", IsError = true };

        var images = provider.CaptureMultiAngle();
        string[] angleNames = ["front", "back", "left", "right", "top"];

        var parts = new List<string>();
        for (int i = 0; i < images.Length; i++)
        {
            var base64 = Convert.ToBase64String(images[i]);
            parts.Add($"[{angleNames[i]}:image:png;base64]{base64}");
        }

        return new ToolHandlerResult
        {
            Content = string.Join("\n", parts),
        };
    }
}
