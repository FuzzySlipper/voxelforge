using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Find/replace colors in the palette. Matches palette entries by color and
/// replaces with a target color. Supports exact and fuzzy (OKLAB) matching.
/// </summary>
public sealed class PaletteMapConsoleCommand : IConsoleCommand
{
    public string Name => "palette-map";
    public string[] Aliases => ["pmap"];
    public string HelpText =>
        "Color find/replace. Usage: palette-map <from> <to> [tolerance]\n" +
        "  Colors: R,G,B or #RRGGBB. Tolerance: OKLAB distance (0-1, default exact).";

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2)
            return CommandResult.Fail(HelpText);

        if (!RgbaColor.TryParse(args[0], out var fromColor))
            return CommandResult.Fail($"Cannot parse source color: {args[0]}");

        if (!RgbaColor.TryParse(args[1], out var toColor))
            return CommandResult.Fail($"Cannot parse target color: {args[1]}");

        float tolerance = 0f;
        if (args.Length >= 3 && !float.TryParse(args[2], out tolerance))
            return CommandResult.Fail($"Invalid tolerance: {args[2]}");

        var palette = context.Model.Palette;
        var changes = new List<(byte Index, MaterialDef Old, MaterialDef New)>();

        foreach (var (idx, mat) in palette.Entries)
        {
            bool match = tolerance <= 0f
                ? mat.Color == fromColor
                : RgbaColor.OklabDistance(mat.Color, fromColor) <= tolerance;

            if (match)
            {
                var newDef = new MaterialDef
                {
                    Name = mat.Name,
                    Color = toColor,
                    Metadata = mat.Metadata,
                };
                changes.Add((idx, mat, newDef));
            }
        }

        if (changes.Count == 0)
        {
            string mode = tolerance > 0 ? $" (tolerance {tolerance})" : "";
            return CommandResult.Fail($"No palette entries match ({fromColor.R},{fromColor.G},{fromColor.B}){mode}.");
        }

        var cmd = new App.Commands.PaletteMapCommand(palette, changes);
        context.UndoStack.Execute(cmd);
        context.Events.Publish(new PaletteChangedEvent(
            PaletteChangeKind.Mapped,
            $"Mapped {changes.Count} palette entry(s)",
            null,
            changes.Count));

        var lines = new List<string>
        {
            $"Replaced {changes.Count} palette entry(s): ({fromColor.R},{fromColor.G},{fromColor.B}) -> ({toColor.R},{toColor.G},{toColor.B})"
        };
        foreach (var (idx, old, _) in changes)
            lines.Add($"  [{idx,3}] {old.Name}");

        return CommandResult.Ok(string.Join("\n", lines));
    }
}
