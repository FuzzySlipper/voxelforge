using VoxelForge.App.Events;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Toggles the measurement grid overlay and configures voxels-per-meter scale.
/// </summary>
public sealed class MeasureCommand : IConsoleCommand
{
    private readonly EditorConfigState _config;

    public string Name => "measure";
    public string[] Aliases => [];
    public string HelpText =>
        "Measurement grid. Usage: measure [on|off|toggle] | measure scale <voxelsPerMeter>\n" +
        "  Shows wireframe cubes at 1-meter intervals using the configured scale.";

    public MeasureCommand(EditorConfigState config) => _config = config;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0 || args[0] == "toggle")
        {
            var oldValue = _config.ShowMeasureGrid.ToString();
            _config.ShowMeasureGrid = !_config.ShowMeasureGrid;
            context.Events.Publish(new ConfigChangedEvent(
                "showMeasureGrid",
                oldValue,
                _config.ShowMeasureGrid.ToString(),
                false));
            return CommandResult.Ok($"Measure grid {(_config.ShowMeasureGrid ? "ON" : "OFF")} (voxelsPerMeter={_config.VoxelsPerMeter})");
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
            {
                var oldValue = _config.ShowMeasureGrid.ToString();
                _config.ShowMeasureGrid = true;
                context.Events.Publish(new ConfigChangedEvent(
                    "showMeasureGrid",
                    oldValue,
                    _config.ShowMeasureGrid.ToString(),
                    false));
                return CommandResult.Ok($"Measure grid ON (voxelsPerMeter={_config.VoxelsPerMeter})");
            }

            case "off":
            {
                var oldValue = _config.ShowMeasureGrid.ToString();
                _config.ShowMeasureGrid = false;
                context.Events.Publish(new ConfigChangedEvent(
                    "showMeasureGrid",
                    oldValue,
                    _config.ShowMeasureGrid.ToString(),
                    false));
                return CommandResult.Ok("Measure grid OFF");
            }

            case "scale":
                if (args.Length < 2 || !float.TryParse(args[1], out float vpm) || vpm <= 0)
                    return CommandResult.Fail("Usage: measure scale <voxelsPerMeter>");
                var oldVoxelsPerMeter = _config.VoxelsPerMeter.ToString();
                var oldShowMeasureGrid = _config.ShowMeasureGrid.ToString();
                _config.VoxelsPerMeter = vpm;
                _config.ShowMeasureGrid = true;
                context.Events.Publish(new ConfigChangedEvent(
                    "voxelsPerMeter",
                    oldVoxelsPerMeter,
                    _config.VoxelsPerMeter.ToString(),
                    false));
                context.Events.Publish(new ConfigChangedEvent(
                    "showMeasureGrid",
                    oldShowMeasureGrid,
                    _config.ShowMeasureGrid.ToString(),
                    false));
                return CommandResult.Ok($"VoxelsPerMeter = {vpm}. Measure grid ON.");

            default:
                return CommandResult.Fail(HelpText);
        }
    }
}
