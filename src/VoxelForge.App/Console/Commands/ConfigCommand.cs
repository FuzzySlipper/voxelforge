using VoxelForge.App.Events;

namespace VoxelForge.App.Console.Commands;

public sealed class ConfigCommand : IConsoleCommand
{
    private readonly EditorConfigState _config;

    public string Name => "config";
    public string[] Aliases => ["cfg"];
    public string HelpText => "View or set config. Usage: config | config <key> <value> | config save";

    public ConfigCommand(EditorConfigState config) => _config = config;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            var lines = new List<string>
            {
                $"  invertOrbitX       = {_config.InvertOrbitX}",
                $"  invertOrbitY       = {_config.InvertOrbitY}",
                $"  orbitSensitivity   = {_config.OrbitSensitivity}",
                $"  zoomSensitivity    = {_config.ZoomSensitivity}",
                $"  defaultGridHint    = {_config.DefaultGridHint}",
                $"  maxUndoDepth       = {_config.MaxUndoDepth}",
                $"  maxZoomDistance    = {_config.MaxZoomDistance}",
                $"  voxelsPerMeter    = {_config.VoxelsPerMeter}",
                $"  backgroundColor    = {string.Join(",", _config.BackgroundColor)}",
            };
            return CommandResult.Ok(string.Join("\n", lines));
        }

        if (args[0] == "save")
        {
            _config.Save();
            context.Events.Publish(new ConfigSavedEvent("config.json"));
            return CommandResult.Ok("Config saved to config.json");
        }

        if (args.Length < 2)
            return CommandResult.Fail("Usage: config <key> <value>");

        var key = args[0].ToLowerInvariant();
        var value = args[1];
        var oldValue = GetConfigValue(key);

        switch (key)
        {
            case "invertorbitx":
                if (!bool.TryParse(value, out var ix)) return CommandResult.Fail("Expected true/false");
                _config.InvertOrbitX = ix;
                break;
            case "invertorbity":
                if (!bool.TryParse(value, out var iy)) return CommandResult.Fail("Expected true/false");
                _config.InvertOrbitY = iy;
                break;
            case "orbitsensitivity":
                if (!float.TryParse(value, out var os)) return CommandResult.Fail("Expected number");
                _config.OrbitSensitivity = os;
                break;
            case "zoomsensitivity":
                if (!float.TryParse(value, out var zs)) return CommandResult.Fail("Expected number");
                _config.ZoomSensitivity = zs;
                break;
            case "defaultgridhint":
                if (!int.TryParse(value, out var dg)) return CommandResult.Fail("Expected integer");
                _config.DefaultGridHint = dg;
                break;
            case "maxundodepth":
                if (!int.TryParse(value, out var mu)) return CommandResult.Fail("Expected integer");
                _config.MaxUndoDepth = mu;
                break;
            case "maxzoomdistance":
                if (!float.TryParse(value, out var mz)) return CommandResult.Fail("Expected number");
                _config.MaxZoomDistance = mz;
                break;
            case "voxelspermeter":
                if (!float.TryParse(value, out var vpm) || vpm <= 0) return CommandResult.Fail("Expected positive number");
                _config.VoxelsPerMeter = vpm;
                break;
            default:
                return CommandResult.Fail($"Unknown config key: '{key}'");
        }

        _config.Save();
        context.Events.Publish(new ConfigChangedEvent(key, oldValue, GetConfigValue(key), true));
        return CommandResult.Ok($"{key} = {value} (saved)");
    }

    private string? GetConfigValue(string key) => key switch
    {
        "invertorbitx" => _config.InvertOrbitX.ToString(),
        "invertorbity" => _config.InvertOrbitY.ToString(),
        "orbitsensitivity" => _config.OrbitSensitivity.ToString(),
        "zoomsensitivity" => _config.ZoomSensitivity.ToString(),
        "defaultgridhint" => _config.DefaultGridHint.ToString(),
        "maxundodepth" => _config.MaxUndoDepth.ToString(),
        "maxzoomdistance" => _config.MaxZoomDistance.ToString(),
        "voxelspermeter" => _config.VoxelsPerMeter.ToString(),
        _ => null,
    };
}
