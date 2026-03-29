using VoxelForge.Core.Screenshot;

namespace VoxelForge.App.Console.Commands;

public sealed class ScreenshotCommand : IConsoleCommand
{
    private const string ScreenshotDir = "content/screenshots";
    private readonly Func<IScreenshotProvider?> _providerFactory;

    public string Name => "screenshot";
    public string[] Aliases => ["ss"];
    public string HelpText => "Capture viewport. Usage: screenshot [filepath] | screenshot all [prefix] | screenshot angle <yaw> <pitch> [filepath]";

    public ScreenshotCommand(Func<IScreenshotProvider?> providerFactory)
    {
        _providerFactory = providerFactory;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        var provider = _providerFactory();
        if (provider is null)
            return CommandResult.Fail("Screenshot provider not available (headless without GPU?).");

        var writeFiles = context.Mode == ExecutionMode.Interactive;
        if (writeFiles)
            Directory.CreateDirectory(ScreenshotDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        if (args.Length == 0)
        {
            var bytes = provider.CaptureViewport();
            if (writeFiles)
            {
                var path = Path.Combine(ScreenshotDir, $"viewport_{timestamp}.png");
                File.WriteAllBytes(path, bytes);
                return CommandResult.Ok($"Saved screenshot to {path} ({bytes.Length} bytes)", bytes);
            }
            return CommandResult.Ok($"Captured viewport ({bytes.Length} bytes)", bytes);
        }

        if (args[0] == "all")
        {
            var prefix = args.Length >= 2 ? args[1] : timestamp;
            string[] names = ["front", "back", "left", "right", "top"];
            var images = provider.CaptureMultiAngle();

            if (writeFiles)
            {
                var paths = new List<string>();
                for (int i = 0; i < images.Length; i++)
                {
                    var path = Path.Combine(ScreenshotDir, $"{prefix}_{names[i]}.png");
                    File.WriteAllBytes(path, images[i]);
                    paths.Add(path);
                }
                return CommandResult.Ok($"Saved {images.Length} screenshots:\n" +
                    string.Join("\n", paths.Select(p => $"  {p}")), images);
            }
            return CommandResult.Ok($"Captured {images.Length} views", images);
        }

        if (args[0] == "angle" && args.Length >= 3)
        {
            if (!float.TryParse(args[1], out float yaw) || !float.TryParse(args[2], out float pitch))
                return CommandResult.Fail("Invalid angle. Expected: screenshot angle <yaw> <pitch>");

            var bytes = provider.CaptureFromAngle(yaw, pitch);
            if (writeFiles)
            {
                var path = args.Length >= 4
                    ? args[3]
                    : Path.Combine(ScreenshotDir, $"angle_{yaw:F0}_{pitch:F0}_{timestamp}.png");
                File.WriteAllBytes(path, bytes);
                return CommandResult.Ok($"Saved screenshot to {path} ({bytes.Length} bytes)", bytes);
            }
            return CommandResult.Ok($"Captured angle ({bytes.Length} bytes)", bytes);
        }

        // Single arg = filepath
        {
            var bytes = provider.CaptureViewport();
            if (writeFiles)
            {
                var path = args[0];
                File.WriteAllBytes(path, bytes);
                return CommandResult.Ok($"Saved screenshot to {path} ({bytes.Length} bytes)", bytes);
            }
            return CommandResult.Ok($"Captured viewport ({bytes.Length} bytes)", bytes);
        }
    }
}
