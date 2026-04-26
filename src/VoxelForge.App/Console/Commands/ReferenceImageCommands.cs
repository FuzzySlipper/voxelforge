using VoxelForge.App.Reference;

namespace VoxelForge.App.Console.Commands;

public sealed class ImgLoadCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;

    public string Name => "imgload";
    public string[] Aliases => [];
    public string HelpText => "Load a reference image. Usage: imgload <filepath>";

    public ImgLoadCommand(ReferenceImageState referenceImageState) => _referenceImageState = referenceImageState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: imgload <filepath>");

        var path = args[0];
        if (!File.Exists(path))
            return CommandResult.Fail($"File not found: {path}");

        var bytes = File.ReadAllBytes(path);
        _referenceImageState.Add(new ReferenceImageEntry { FilePath = path, RawBytes = bytes });
        _referenceImageState.NotifyChanged();

        int idx = _referenceImageState.Images.Count - 1;
        return CommandResult.Ok($"Loaded image [{idx}] {Path.GetFileName(path)} ({bytes.Length} bytes)");
    }
}

public sealed class ImgListCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;

    public string Name => "imglist";
    public string[] Aliases => [];
    public string HelpText => "List loaded reference images.";

    public ImgListCommand(ReferenceImageState referenceImageState) => _referenceImageState = referenceImageState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (_referenceImageState.Images.Count == 0)
            return CommandResult.Ok("No reference images loaded.");

        var lines = new List<string>();
        for (int i = 0; i < _referenceImageState.Images.Count; i++)
        {
            var img = _referenceImageState.Images[i];
            var size = img.RawBytes.Length < 1024 ? $"{img.RawBytes.Length} B" : $"{img.RawBytes.Length / 1024} KB";
            lines.Add($"  [{i}] {img.Label} ({size})");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class ImgRemoveCommand : IConsoleCommand
{
    private readonly ReferenceImageState _referenceImageState;

    public string Name => "imgremove";
    public string[] Aliases => [];
    public string HelpText => "Remove a reference image. Usage: imgremove <index>";

    public ImgRemoveCommand(ReferenceImageState referenceImageState) => _referenceImageState = referenceImageState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: imgremove <index>");

        if (_referenceImageState.Get(idx) is null)
            return CommandResult.Fail($"No image at index {idx}.");

        _referenceImageState.RemoveAt(idx);
        _referenceImageState.NotifyChanged();
        return CommandResult.Ok($"Removed image [{idx}].");
    }
}
