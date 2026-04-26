using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

public sealed class PaletteCommand : IConsoleCommand
{
    public string Name => "palette";
    public string[] Aliases => ["pal"];
    public string HelpText => "List palette entries, or add one. Usage: palette | palette add <index> <name> <r> <g> <b>";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            if (context.Model.Palette.Count == 0)
                return CommandResult.Ok("Palette is empty.");

            var lines = new List<string>();
            foreach (var (idx, mat) in context.Model.Palette.Entries.OrderBy(e => e.Key))
                lines.Add($"  [{idx,3}] {mat.Name,-20} ({mat.Color.R},{mat.Color.G},{mat.Color.B},{mat.Color.A})");

            return CommandResult.Ok(string.Join("\n", lines));
        }

        if (args[0] == "add" && args.Length >= 6)
        {
            if (!byte.TryParse(args[1], out byte idx) || !byte.TryParse(args[3], out byte r) ||
                !byte.TryParse(args[4], out byte g) || !byte.TryParse(args[5], out byte b))
                return CommandResult.Fail("Invalid arguments. Expected: palette add <index> <name> <r> <g> <b>");

            byte a = args.Length >= 7 && byte.TryParse(args[6], out byte alpha) ? alpha : (byte)255;
            context.Model.Palette.Set(idx, new MaterialDef
            {
                Name = args[2],
                Color = new RgbaColor(r, g, b, a),
            });
            context.Events.Publish(new PaletteChangedEvent(
                PaletteChangeKind.EntryAdded,
                $"Added palette[{idx}] = {args[2]}",
                idx,
                1));
            return CommandResult.Ok($"Added palette[{idx}] = {args[2]} ({r},{g},{b},{a})");
        }

        return CommandResult.Fail("Usage: palette | palette add <index> <name> <r> <g> <b> [a]");
    }
}
