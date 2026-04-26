using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Toggles the measurement grid overlay and configures voxels-per-meter scale.
/// </summary>
public sealed class MeasureCommand : IConsoleCommand
{
    private readonly EditorConfigState _config;
    private readonly EditorConfigService _configService;

    public string Name => "measure";
    public string[] Aliases => [];
    public string HelpText =>
        "Measurement grid. Usage: measure [on|off|toggle] | measure scale <voxelsPerMeter>\n" +
        "  Shows wireframe cubes at 1-meter intervals using the configured scale.";

    public MeasureCommand(EditorConfigState config, EditorConfigService configService)
    {
        _config = config;
        _configService = configService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0 || args[0] == "toggle")
        {
            var result = _configService.SetMeasureGrid(
                _config,
                context.Events,
                new SetMeasureGridRequest(!_config.ShowMeasureGrid, null));
            return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
        }

        switch (args[0].ToLowerInvariant())
        {
            case "on":
            {
                var result = _configService.SetMeasureGrid(
                    _config,
                    context.Events,
                    new SetMeasureGridRequest(true, null));
                return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
            }

            case "off":
            {
                var result = _configService.SetMeasureGrid(
                    _config,
                    context.Events,
                    new SetMeasureGridRequest(false, null));
                return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
            }

            case "scale":
            {
                if (args.Length < 2 || !float.TryParse(args[1], out float vpm))
                    return CommandResult.Fail("Usage: measure scale <voxelsPerMeter>");

                var result = _configService.SetMeasureGrid(
                    _config,
                    context.Events,
                    new SetMeasureGridRequest(true, vpm));
                return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
            }

            default:
                return CommandResult.Fail(HelpText);
        }
    }
}
