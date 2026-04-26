using VoxelForge.App.Reference;
using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class ImgLoadCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;
    private readonly ReferenceAssetService _referenceAssetService;

    public string Name => "imgload";
    public string[] Aliases => [];
    public string HelpText => "Load a reference image. Usage: imgload <filepath>";

    public ImgLoadCommand(ReferenceImageState referenceImageState, ReferenceAssetService referenceAssetService)
    {
        _referenceImageState = referenceImageState;
        _referenceAssetService = referenceAssetService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: imgload <filepath>");

        var result = _referenceAssetService.LoadImage(
            _referenceImageState,
            context.Events,
            new LoadReferenceAssetRequest(args[0]));
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}

public sealed class ImgListCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;
    private readonly ReferenceAssetService _referenceAssetService;

    public string Name => "imglist";
    public string[] Aliases => [];
    public string HelpText => "List loaded reference images.";

    public ImgListCommand(ReferenceImageState referenceImageState, ReferenceAssetService referenceAssetService)
    {
        _referenceImageState = referenceImageState;
        _referenceAssetService = referenceAssetService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var result = _referenceAssetService.ListImages(_referenceImageState);
        if (result.Data is null || result.Data.Count == 0)
            return CommandResult.Ok(result.Message);

        var lines = new List<string>();
        for (int i = 0; i < result.Data.Count; i++)
        {
            var image = result.Data[i];
            var size = image.ByteCount < 1024 ? $"{image.ByteCount} B" : $"{image.ByteCount / 1024} KB";
            lines.Add($"  [{image.Position}] {image.Label} ({size})");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class ImgRemoveCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;
    private readonly ReferenceAssetService _referenceAssetService;

    public string Name => "imgremove";
    public string[] Aliases => [];
    public string HelpText => "Remove a reference image. Usage: imgremove <index>";

    public ImgRemoveCommand(ReferenceImageState referenceImageState, ReferenceAssetService referenceAssetService)
    {
        _referenceImageState = referenceImageState;
        _referenceAssetService = referenceAssetService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: imgremove <index>");

        var result = _referenceAssetService.RemoveImage(
            _referenceImageState,
            context.Events,
            new RemoveReferenceAssetRequest(idx));
        return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
    }
}
