namespace VoxelForge.App.Console.Commands;

public sealed class ConfigCommand : IConsoleCommand
{
    private readonly EditorConfig _config;

    public string Name => "config";
    public string[] Aliases => ["cfg"];
    public string HelpText => "View or set config. Usage: config | config <key> <value> | config save";

    public ConfigCommand(EditorConfig config) => _config = config;

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
                $"  backgroundColor    = {string.Join(",", _config.BackgroundColor)}",
            };
            return CommandResult.Ok(string.Join("\n", lines));
        }

        if (args[0] == "save")
        {
            _config.Save();
            return CommandResult.Ok("Config saved to config.json");
        }

        if (args.Length < 2)
            return CommandResult.Fail("Usage: config <key> <value>");

        var key = args[0].ToLowerInvariant();
        var value = args[1];

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
            default:
                return CommandResult.Fail($"Unknown config key: '{key}'");
        }

        _config.Save();
        return CommandResult.Ok($"{key} = {value} (saved)");
    }
}
