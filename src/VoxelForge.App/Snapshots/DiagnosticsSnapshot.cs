using VoxelForge.App.Events;

namespace VoxelForge.App.Snapshots;

/// <summary>
/// Renderer-neutral diagnostic status snapshot.
/// Captures severity, source, and message from recent <see cref="EditorStatusEvent"/> entries
/// and a snapshot timestamp for correlation.
/// </summary>
public sealed class DiagnosticsSnapshot
{
    /// <summary>
    /// Timestamp of this snapshot in UTC.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Recent status events captured since the last snapshot, ordered oldest-first.
    /// May be empty if no status events have been emitted.
    /// </summary>
    public required IReadOnlyList<StatusEntrySnapshot> RecentStatuses { get; init; }
}

/// <summary>
/// A single diagnostic status entry.
/// </summary>
public sealed class StatusEntrySnapshot
{
    /// <summary>
    /// Severity level: "info", "warning", or "error".
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Source subsystem that emitted the status.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Human-readable status message.
    /// </summary>
    public required string Message { get; init; }
}