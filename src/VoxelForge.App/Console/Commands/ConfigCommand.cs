using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class ConfigCommand : IConsoleCommand
{
    private readonly EditorConfigState _config;
    private readonly EditorConfigService _configService;

    public string Name => "config";
    public string[] Aliases => ["cfg"];
    public string HelpText => "View or set config. Usage: config | config <key> <value> | config save";

    public ConfigCommand(EditorConfigState config, EditorConfigService configService)
    {
        _config = config;
        _configService = configService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            var result = _configService.List(_config);
            if (result.Data is null)
                return CommandResult.Fail(result.Message);

            var lines = new List<string>();
            for (int i = 0; i < result.Data.Count; i++)
                lines.Add($"  {result.Data[i].Key,-18} = {result.Data[i].Value}");
            return CommandResult.Ok(string.Join("\n", lines));
        }

        if (args[0] == "save")
        {
            var saveResult = _configService.Save(_config, context.Events);
            return saveResult.Success ? CommandResult.Ok(saveResult.Message) : CommandResult.Fail(saveResult.Message);
        }

        if (args.Length < 2)
            return CommandResult.Fail("Usage: config <key> <value>");

        var setResult = _configService.SetValue(
            _config,
            context.Events,
            new SetConfigValueRequest(args[0], args[1], Save: true));
        return setResult.Success ? CommandResult.Ok(setResult.Message) : CommandResult.Fail(setResult.Message);
    }
}
