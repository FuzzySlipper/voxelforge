using System.Text.Json;
using VoxelForge.Core.Screenshot;

namespace VoxelForge.Core.LLM.Handlers;

/// <summary>
/// Captures a screenshot of the current viewport and returns it as base64 PNG.
/// Enables the LLM to "see" the voxel model.
/// </summary>
public sealed class ViewModelHandler : IToolHandler
{
    private readonly Func<IScreenshotProvider?> _providerFactory;

    public string ToolName => "view_model";

    public ViewModelHandler(Func<IScreenshotProvider?> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Capture a screenshot of the current voxel model viewport. Returns a base64-encoded PNG image.",
        ParametersSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        var provider = _providerFactory();
        if (provider is null)
            return new ToolHandlerResult { Content = "Screenshot provider not available.", IsError = true };

        var bytes = provider.CaptureViewport();
        var base64 = Convert.ToBase64String(bytes);

        return new ToolHandlerResult
        {
            Content = $"[image:png;base64]{base64}",
        };
    }
}
