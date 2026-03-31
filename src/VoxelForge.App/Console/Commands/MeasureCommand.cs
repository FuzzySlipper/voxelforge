namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Toggles the measurement grid overlay and configures voxels-per-meter scale.
/// </summary>
public sealed class MeasureCommand : IConsoleCommand
{
    private readonly EditorConfig _config;

    public string Name => "measure";
    public string[] Aliases => [];
    public string HelpText =>
        "Measurement grid. Usage: measure [on|off|toggle] | measure scale <voxelsPerMeter>\n" +
        "  Shows wireframe cubes at 1-meter intervals using the configured scale.";

    public MeasureCommand(EditorConfig config) => _config = config;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0 || args[0] == "toggle")
        {
            _config.ShowMeasureGrid = !_config.ShowMeasureGrid;
            return CommandResult.Ok($"Measure grid {(_config.ShowMeasureGrid ? "ON" : "OFF")} (voxelsPerMeter={_config.VoxelsPerMeter})");
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
                _config.ShowMeasureGrid = true;
                return CommandResult.Ok($"Measure grid ON (voxelsPerMeter={_config.VoxelsPerMeter})");

            case "off":
                _config.ShowMeasureGrid = false;
                return CommandResult.Ok("Measure grid OFF");

            case "scale":
                if (args.Length < 2 || !float.TryParse(args[1], out float vpm) || vpm <= 0)
                    return CommandResult.Fail("Usage: measure scale <voxelsPerMeter>");
                _config.VoxelsPerMeter = vpm;
                _config.ShowMeasureGrid = true;
                return CommandResult.Ok($"VoxelsPerMeter = {vpm}. Measure grid ON.");

            default:
                return CommandResult.Fail(HelpText);
        }
    }
}
