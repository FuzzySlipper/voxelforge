using VoxelForge.Core;

namespace VoxelForge.App.Events;

public enum VoxelModelChangeKind
{
    SetVoxel,
    RemoveVoxel,
    MixedVoxelEdit,
    FillRegion,
    Clear,
    GridHintChanged,
    Voxelized,
    VoxelizeComparison,
    Baked,
    PaletteIndexRemap,
    ProjectLoaded,
}

public sealed record VoxelModelChangedEvent(
    VoxelModelChangeKind Kind,
    string Description,
    int? AffectedVoxelCount) : IApplicationEvent;

public enum LabelChangeKind
{
    RegionCreated,
    RegionAssigned,
    RegionUnassigned,
    LabelsRebuilt,
}

public sealed record LabelChangedEvent(
    LabelChangeKind Kind,
    string Description,
    RegionId? RegionId,
    int AffectedVoxelCount) : IApplicationEvent;

public enum PaletteChangeKind
{
    EntryAdded,
    EntryUpdated,
    EntriesChanged,
    EntryRemoved,
    Mapped,
    Reduced,
    TextureChanged,
}

public sealed record PaletteChangedEvent(
    PaletteChangeKind Kind,
    string Description,
    byte? PaletteIndex,
    int AffectedEntryCount) : IApplicationEvent;

public enum AnimationChangeKind
{
    FrameAdded,
}

public sealed record AnimationChangedEvent(
    AnimationChangeKind Kind,
    string Description,
    int? ClipIndex,
    int? FrameIndex) : IApplicationEvent;

public sealed record ProjectSavedEvent(
    string Path,
    int ByteCount) : IApplicationEvent;

public sealed record ProjectLoadedEvent(
    string Path,
    string Name,
    int VoxelCount,
    int RegionCount,
    int ClipCount) : IApplicationEvent;

public enum UndoHistoryChangeKind
{
    Executed,
    Undone,
    Redone,
}

public sealed record UndoHistoryChangedEvent(
    UndoHistoryChangeKind Kind,
    string CommandDescription,
    bool CanUndo,
    bool CanRedo) : IApplicationEvent;

public enum ReferenceModelChangeKind
{
    Loaded,
    Removed,
    Cleared,
    TransformChanged,
    RenderModeChanged,
    VisibilityChanged,
    AnimationChanged,
    TextureChanged,
}

public sealed record ReferenceModelChangedEvent(
    ReferenceModelChangeKind Kind,
    string Description,
    int? ModelIndex) : IApplicationEvent;

public enum ReferenceImageChangeKind
{
    Loaded,
    Removed,
}

public sealed record ReferenceImageChangedEvent(
    ReferenceImageChangeKind Kind,
    string Description,
    int? ImageIndex) : IApplicationEvent;

public sealed record ConfigChangedEvent(
    string Key,
    string? OldValue,
    string? NewValue,
    bool Saved) : IApplicationEvent;

public sealed record ConfigSavedEvent(string Path) : IApplicationEvent;
