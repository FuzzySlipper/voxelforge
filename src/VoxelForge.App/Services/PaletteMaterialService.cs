using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core;

namespace VoxelForge.App.Services;

public readonly record struct AddPaletteMaterialRequest(byte PaletteIndex, string Name, byte Red, byte Green, byte Blue, byte Alpha);

public readonly record struct SetPaletteMaterialRequest(
    byte PaletteIndex,
    MaterialDef Material,
    PaletteChangeKind ChangeKind,
    string Description);

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
        UndoStack undoStack,
        IEventPublisher events,
        AddPaletteMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Name);

        if (request.PaletteIndex == 0)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = "Palette index 0 is reserved for air.",
            };
        }

        bool existed = model.Palette.Contains(request.PaletteIndex);
        var material = new MaterialDef
        {
            Name = request.Name,
            Color = new RgbaColor(request.Red, request.Green, request.Blue, request.Alpha),
        };
        var changeKind = existed ? PaletteChangeKind.EntryUpdated : PaletteChangeKind.EntryAdded;
        var result = SetMaterial(
            model,
            undoStack,
            events,
            new SetPaletteMaterialRequest(
                request.PaletteIndex,
                material,
                changeKind,
                $"Set palette[{request.PaletteIndex}] = {request.Name}"));

        if (!result.Success)
            return result;

        string action = existed ? "Updated" : "Added";
        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"{action} palette[{request.PaletteIndex}] = {request.Name} ({request.Red},{request.Green},{request.Blue},{request.Alpha})",
            Events = result.Events,
        };
    }

    public ApplicationServiceResult SetMaterial(
        VoxelModel model,
        UndoStack undoStack,
        IEventPublisher events,
        SetPaletteMaterialRequest request)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Material);
        ArgumentNullException.ThrowIfNull(request.Description);

        if (request.PaletteIndex == 0)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = "Palette index 0 is reserved for air.",
            };
        }

        undoStack.Execute(new SetPaletteMaterialCommand(
            model.Palette,
            request.PaletteIndex,
            request.Material,
            request.Description));

        var applicationEvents = new IApplicationEvent[]
        {
            new PaletteChangedEvent(
                request.ChangeKind,
                request.Description,
                request.PaletteIndex,
                1),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = request.Description,
            Events = applicationEvents,
        };
    }

    private static int CompareByPaletteIndex(PaletteMaterialListEntry left, PaletteMaterialListEntry right)
    {
        return left.PaletteIndex.CompareTo(right.PaletteIndex);
    }
}
