using VoxelForge.App.Services;

namespace VoxelForge.App.Console.Commands;

public sealed class PaletteCommand : IConsoleCommand
{
    private readonly PaletteMaterialService _paletteMaterialService;

    public string Name => "palette";
    public string[] Aliases => ["pal"];
    public string HelpText => "List palette entries, or add one. Usage: palette | palette add <index> <name> <r> <g> <b>";

    public PaletteCommand(PaletteMaterialService paletteMaterialService)
    {
        _paletteMaterialService = paletteMaterialService;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            var result = _paletteMaterialService.ListMaterials(context.Model.Palette);
            if (result.Data is null || result.Data.Count == 0)
                return CommandResult.Ok(result.Message);

            var lines = new List<string>();
            for (int i = 0; i < result.Data.Count; i++)
            {
                var entry = result.Data[i];
                lines.Add($"  [{entry.PaletteIndex,3}] {entry.Name,-20} ({entry.Color.R},{entry.Color.G},{entry.Color.B},{entry.Color.A})");
            }

            return CommandResult.Ok(string.Join("\n", lines));
        }

        if (args[0] == "add" && args.Length >= 6)
        {
            if (!byte.TryParse(args[1], out byte idx) || !byte.TryParse(args[3], out byte r) ||
                !byte.TryParse(args[4], out byte g) || !byte.TryParse(args[5], out byte b))
                return CommandResult.Fail("Invalid arguments. Expected: palette add <index> <name> <r> <g> <b>");

            byte a = args.Length >= 7 && byte.TryParse(args[6], out byte alpha) ? alpha : (byte)255;
            var result = _paletteMaterialService.AddMaterial(
                context.Model,
                context.Events,
                new AddPaletteMaterialRequest(idx, args[2], r, g, b, a));
            return result.Success ? CommandResult.Ok(result.Message) : CommandResult.Fail(result.Message);
        }

        return CommandResult.Fail("Usage: palette | palette add <index> <name> <r> <g> <b> [a]");
    }
}
