using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

public readonly record struct AddPaletteMaterialRequest(byte PaletteIndex, string Name, byte Red, byte Green, byte Blue, byte Alpha);

public readonly record struct PaletteMaterialListEntry(byte PaletteIndex, string Name, RgbaColor Color);

/// <summary>
/// Stateless service for palette/material edits and palette queries.
/// </summary>
public sealed class PaletteMaterialService
{
    public ApplicationServiceResult<IReadOnlyList<PaletteMaterialListEntry>> ListMaterials(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var entries = new List<PaletteMaterialListEntry>();
        foreach (var entry in palette.Entries)
            entries.Add(new PaletteMaterialListEntry(entry.Key, entry.Value.Name, entry.Value.Color));
        entries.Sort(CompareByPaletteIndex);

        return new ApplicationServiceResult<IReadOnlyList<PaletteMaterialListEntry>>
        {
            Success = true,
            Message = entries.Count == 0 ? "Palette is empty." : "Palette entries.",
            Data = entries,
        };
    }

    public ApplicationServiceResult AddMaterial(
        VoxelModel model,
        IEventPublisher events,
        AddPaletteMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Name);

        model.Palette.Set(request.PaletteIndex, new MaterialDef
        {
            Name = request.Name,
            Color = new RgbaColor(request.Red, request.Green, request.Blue, request.Alpha),
        });

        var applicationEvents = new IApplicationEvent[]
        {
            new PaletteChangedEvent(
                PaletteChangeKind.EntryAdded,
                $"Added palette[{request.PaletteIndex}] = {request.Name}",
                request.PaletteIndex,
                1),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Added palette[{request.PaletteIndex}] = {request.Name} ({request.Red},{request.Green},{request.Blue},{request.Alpha})",
            Events = applicationEvents,
        };
    }

    private static int CompareByPaletteIndex(PaletteMaterialListEntry left, PaletteMaterialListEntry right)
    {
        return left.PaletteIndex.CompareTo(right.PaletteIndex);
    }
}
