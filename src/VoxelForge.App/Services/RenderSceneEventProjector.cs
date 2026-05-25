using VoxelForge.App.Events;
using VoxelForge.App.Workspaces;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service that projects <see cref="IApplicationEvent"/> facts into
/// render-scene semantic events. Maintains a monotonic event sequence via the
/// workspace state revision counter.
/// <para>
/// Transport hosts (MCP via SSE, Bridge via den-bridge) publish the projected
/// events to their respective subscribers. The event names and meanings are
/// shared across transports.
/// </para>
/// </summary>
public sealed class RenderSceneEventProjector
{
    /// <summary>
    /// Project an application event into zero or more render-scene events.
    /// </summary>
    public IReadOnlyList<RenderSceneEvent> Project(
        IApplicationEvent applicationEvent,
        VoxelForgeWorkspaceState workspace)
    {
        ArgumentNullException.ThrowIfNull(applicationEvent);
        ArgumentNullException.ThrowIfNull(workspace);

        var sequence = workspace.Revision;
        var results = new List<RenderSceneEvent>(1);

        switch (applicationEvent)
        {
            case VoxelModelChangedEvent:
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.mesh_changed",
                    Sequence = sequence,
                    Description = "Voxel model mesh changed",
                });
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.snapshot_required",
                    Sequence = sequence,
                    Description = "Mesh change requires full snapshot refresh",
                });
                break;

            case PaletteChangedEvent:
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.palette_changed",
                    Sequence = sequence,
                    Description = "Palette entries changed",
                });
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.snapshot_required",
                    Sequence = sequence,
                    Description = "Palette change requires snapshot refresh",
                });
                break;

            case UndoHistoryChangedEvent:
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.state_changed",
                    Sequence = sequence,
                    Description = "Undo history changed",
                });
                break;

            case ProjectLoadedEvent:
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.state_changed",
                    Sequence = sequence,
                    Description = "Project loaded",
                });
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.snapshot_required",
                    Sequence = sequence,
                    Description = "Project load requires full snapshot",
                });
                break;

            case ReferenceModelChangedEvent:
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.reference_changed",
                    Sequence = sequence,
                    Description = "Reference model state changed",
                });
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.snapshot_required",
                    Sequence = sequence,
                    Description = "Reference change requires snapshot refresh",
                });
                break;

            default:
                // Unknown events produce a generic state change event
                results.Add(new RenderSceneEvent
                {
                    Kind = "render.state_changed",
                    Sequence = sequence,
                    Description = $"Unclassified event: {applicationEvent.GetType().Name}",
                });
                break;
        }

        return results;
    }
}

/// <summary>
/// A semantic render-scene event for transport to viewers.
/// </summary>
public sealed class RenderSceneEvent
{
    /// <summary>Event kind: "render.state_changed", "render.mesh_changed", "render.palette_changed", "render.reference_changed", "render.snapshot_required", "render.diagnostics".</summary>
    public required string Kind { get; init; } = "render.state_changed";

    /// <summary>Monotonic sequence number from the workspace revision counter.</summary>
    public required long Sequence { get; init; }

    /// <summary>Human-readable description of the event.</summary>
    public required string Description { get; init; } = "";
}
