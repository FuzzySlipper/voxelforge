# FNA/Myra Incremental Mesh Backport — Evaluation

> Task: #1200 | Follow-up from: #1181 (Task D)
> Branch: `task/1200-fna-incremental-mesh-backport`
> Base commit: `94a0aadd3ae31d6b967625715937e1f43062f916`
> Status: **Deferred — documented blockers**
> Last updated: 2026-05-06

## Executive Summary

**Recommendation: Defer the backport.** The incremental mesh-region pipeline (`MeshRegionService`, dirty-region tracking) developed for the Electron experiment cannot be safely backported to the FNA/Myra control path in its current form. Three independent architectural blockers exist, and the current `MeshRegionService.BuildIncrementalUpdate()` implementation would deliver **zero performance benefit** for FNA because it still builds the full mesh snapshot before extracting per-region subsets.

## Acceptance Criteria Assessment

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Assess whether `MeshRegionService`/dirty-region tracking can be used safely by FNA/Myra renderer | ✅ Assessed — not safe in current form | Three blockers documented below |
| If implementing: rebuild only dirty mesh regions after voxel edits while preserving visual correctness | ❌ Not implemented — blocked | Deceptive `BuildIncrementalUpdate` still builds full mesh |
| If deferring: document concrete blockers, expected benefit/risk, and prerequisites | ✅ Documented | This document |
| Existing FNA/Myra behavior and architecture tests continue to pass | ✅ 398 tests pass, 0 failures | Baseline verified |
| Task outcome recorded in docs so future agents do not re-litigate | ✅ This document | `docs/architecture/fna-incremental-mesh-backport-evaluation.md` |

## Blocker #1: `VoxelModelChangedEvent` Lacks Changed-Bounds Metadata

### Current State

`VoxelModelChangedEvent` carries only:

```csharp
public sealed record VoxelModelChangedEvent(
    VoxelModelChangeKind Kind,
    string Description,
    int? AffectedVoxelCount) : IApplicationEvent;
```

There is no `Point3 MinChanged`, `Point3 MaxChanged`, or list of affected positions. This means any consumer — including the FNA dirty handler — cannot determine *which* voxels changed without either:

1. Maintaining a previous model snapshot and diffing (O(voxels) — same order as full mesh rebuild)
2. Conservatively assuming all occupied regions are dirty (which is what the bridge does today)

### Evidence from Bridge Code

The `MeshSubscriptionManager`'s `IEventHandler<VoxelModelChangedEvent>.Handle()` is intentionally a no-op with this comment:

```csharp
// Intentionally a no-op. Dirty region recording for mesh updates is done
// synchronously by the bridge command handlers (CommandExecuteHandler,
// HistoryUndoHandler, HistoryRedoHandler, ProjectLoadHandler), which call
// RecordFullDirty() directly after mutating the model. The event-based path
// is reserved for future enhancement when VoxelModelChangedEvent carries
// exact changed-bounds metadata, enabling finer-grained dirty region tracking.
```

The bridge has already identified this exact blocker and works around it by calling `RecordFullDirty()` from command handlers — which marks *all occupied regions* dirty. This is safe for correctness but defeats the purpose of incremental updates in terms of CPU savings.

### Fixing This

Adding bounds to `VoxelModelChangedEvent` requires:

1. Add `Point3? ChangeMin` / `Point3? ChangeMax` fields (or a `HashSet<Point3>? ChangedVoxels`)
2. Update all event publishers (command execution, undo/redo, fill region, clear, etc.) to populate these fields
3. This is a **prerequisite** for any incremental approach — FNA or otherwise

## Blocker #2: `IVoxelMesher` Has No Region-Bounded Meshing

### Current State

`IVoxelMesher` exposes a single method:

```csharp
public interface IVoxelMesher
{
    VoxelMesh Build(VoxelModel model);
}
```

Both `GreedyMesher` and `NaiveMesher` build geometry from the **entire model**. There is no overload to build mesh only for voxels within a given bounding box.

### Why This Matters

Without region-bounded meshing, any "incremental" update still requires either:
- **Full mesh build** (what `MeshRegionService.BuildIncrementalUpdate` does today — it builds the full snapshot, then extracts per-region subsets)
- **Manual region clipping** after full mesh build (same CPU cost)

The `GreedyMesher` is particularly problematic for bounded meshing because its greedy merge algorithm sweeps slices across the entire bounding box. A bounded variant would need to:
1. Sweep only within the specified region bounds (which may improve cache behavior for small regions)
2. Handle face-sharing on region boundaries (a voxel at the edge of a region may have a face that extends beyond the region's bounds to cover an adjacent region's air voxels)

### Fixing This

Adding region-bounded meshing requires:

1. Adding a new method to `IVoxelMesher`: `Build(VoxelModel model, RegionBounds bounds)` or similar
2. Implementing bounded variants in `GreedyMesher` and `NaiveMesher`
3. Both implementations are non-trivial — the greedy mesher in particular uses axis sweeps over the full bounding box and would need to be adapted for sub-volume sweeps
4. This change lives in `VoxelForge.Core` — the architecture boundary is clear but the meshing logic is intricate

## Blocker #3: `VoxelRenderer` Uses Monolithic GPU Buffers

### Current State

`VoxelRenderer` owns exactly one `VertexBuffer` and one `IndexBuffer`:

```csharp
private VertexBuffer? _vertexBuffer;
private IndexBuffer? _indexBuffer;
```

`RebuildBuffers()` disposes both and creates new ones from scratch. There is no mechanism for partial buffer updates.

### Why This Matters

To use incremental region updates, the FNA renderer would need one of:

| Approach | Change Required | Risk |
|----------|----------------|------|
| **Per-region GPU buffers** | Many `VertexBuffer`/`IndexBuffer` objects, tracked per region; `Draw` loops over all regions | Draw-call overhead with many small buffers; GPU driver overhead for many buffer objects |
| **Single buffer with partial `SetData`** | Pre-allocated buffer large enough for max mesh; `SetData` with offset for region updates | Buffer reallocation still needed if mesh grows beyond initial size; partial `SetData` requires careful vertex/index offset tracking |
| **Combined approach** | Hybrid: small number of contiguous buffer regions | Most complex to implement and reason about |

All three approaches are significant refactorings to `VoxelRenderer` with unknown performance characteristics. The FNA3D driver layer (via SDL3/Vulkan) may handle many small buffers differently from a single large one, and draw-call overhead matters.

### Fixing This

A full incremental renderer refactoring requires:
1. Decide on buffer strategy (per-region vs partial-update)
2. Add region-coordinate tracking to the renderer
3. Replace `MarkDirty()` / `RebuildBuffers()` with per-region dirty tracking and partial rebuild logic
4. Handle edge cases: model growth, model shrink, complete model replacement, project load

## The `MeshRegionService.BuildIncrementalUpdate()` Deception

The most important finding: `MeshRegionService.BuildIncrementalUpdate` **does not actually rebuild only dirty regions**. Its implementation:

```csharp
var fullSnapshot = new MeshSnapshotService(_mesher).BuildSnapshot(model);
// ... then extract per-region subsets from the full snapshot
```

This means:
- **Full mesh CPU cost**: The greedy mesher runs over the entire model
- **Full mesh memory cost**: The flat vertex/index arrays are allocated for the entire model
- **Zero CPU savings**: Whether you have 1 dirty region or 100, the full mesh is built every time

The only benefit is on the *receiver side* — the Electron/Three.js renderer can upload only the changed region's buffers instead of rebuilding its entire Three.js scene. This is a serialization/upload optimization, not a CPU-side optimization.

For the FNA/Myra path, which is in-process and doesn't serialize over a WebSocket, there is **no benefit** from this pattern. You still do all the work, you just add complexity to extract per-region subsets.

## Expected Benefit vs Risk

### If Implemented (after all blockers are resolved)

| Benefit | Magnitude | Evidence |
|---------|-----------|----------|
| Reduced CPU time for small edits on large models | Medium | For a ~100k-voxel scene, a 1-voxel edit would only remesh ~16³ = 4096-voxel region instead of the full model |
| Lower per-frame GPU upload cost | Low | FNA is in-process; GPU buffer upload is already fast |
| Smoother editing UX on large models | Medium | Sub-50ms edits may become sub-5ms for single-voxel operations |

### Current Risk (without resolving blockers)

| Risk | Severity | Explanation |
|------|----------|-------------|
| **Zero-performance backport** | High | Implementing the current `MeshRegionService` pattern in FNA would add code complexity with identical CPU cost — a net negative |
| **Deceptive "incremental" claim** | High | Marking a task done with code that calls itself incremental but still builds the full mesh would mislead future developers |
| **Regressed buffer management** | Medium | Adding per-region buffer tracking could introduce GPU resource leaks or draw-call regressions |
| **Architecture boundary violations** | Low | Easily avoided by keeping Core mesher changes clean and Engine renderer changes isolated |

## Prerequisites for a Safe Backport (Ordered)

These are the independent changes that must be made before a safe, honest incremental FNA backport is possible:

### Prerequisite 1: Event Bounds (App Layer)

**Effort:** Medium (add fields to event, update all publishers)

Add `Point3? ChangeMin` and `Point3? ChangeMax` (or a `HashSet<Point3>? ChangedVoxels`) to `VoxelModelChangedEvent`. Update all event publishers:
- `SetVoxelCommand` — bounds = the single voxel position
- `RemoveVoxelCommand` — bounds = the single voxel position
- `FillRegionCommand` — bounds = the fill region
- `ClearCommand` — bounds = entire model bounds
- `UndoStack` commands — aggregate bounds from the undone/redone commands
- Plus: `Voxelized`, `Baked`, `PaletteIndexRemap`, `ProjectLoaded` events

This blocker is independent of meshing or rendering and should be addressed as a separate maintenance task. It also unlocks better incremental pipeline behavior for the Electron bridge path.

### Prerequisite 2: Bounded Meshing (Core Layer)

**Effort:** High (change `IVoxelMesher`, modify `GreedyMesher` and `NaiveMesher`)

Add a bounded meshing method to `IVoxelMesher`. This is the hardest prerequisite because:
- `GreedyMesher` sweeps slices across the full bounding box; a bounded version must sweep only within region bounds
- Face-sharing on region edges must be correct (a voxel's face at a region boundary may be invisible from inside the region but visible from outside)
- Both mesher implementations must produce identical results at region boundaries to avoid visible seams

### Prerequisite 3: Per-Region GPU Buffers (Engine Layer)

**Effort:** High (refactor `VoxelRenderer` data model)

Decide on a buffer strategy and implement it. This is a pure Engine.MonoGame change and does not affect Core or App boundaries, but it is the most implementation-risk-heavy prerequisite because GPU buffer management patterns affect frame-time performance.

### Prerequisite 4: Wire Up Per-Region Rendering

**Effort:** Medium (connect bounded mesher output to per-region GPU buffers)

Once the mesher can produce per-region geometry and the renderer can consume it, wire the two together. This is where the actual "rebuild only dirty regions" pipeline lives.

## Recommendation

**Defer the backport.** The current `MeshRegionService.BuildIncrementalUpdate` provides no CPU-side benefit and would be deceptive if ported as-is. The three blockers are independently significant engineering tasks (especially bounded greedy meshing), and the benchmark data suggests limited practical benefit for the current scene sizes (sub-50ms full rebuilds for scenes up to ~26k voxels).

### What Future Work Would Change This Decision

- A user reports frame drops during editing on large models (>50k voxels)
- A systematic benchmark shows that per-region incremental meshing saves >30% CPU time on realistic edit patterns
- A maintainer decides to invest in `Prerequisite 1` (event bounds) because it also benefits the Electron bridge path and is independent of the FNA rendering decision

### What Should Not Be Done

- Do not backport `MeshRegionService.BuildIncrementalUpdate` as-is to FNA — it still builds the full mesh and provides zero performance benefit
- Do not add per-region GPU buffers before bounded meshing exists — there's nothing to put in them
- Do not claim "incremental mesh updates" for FNA without all three blockers resolved

## Cross-References

- [`docs/architecture/electron-renderer-decision-checkpoint.md`](electron-renderer-decision-checkpoint.md) — Original Task D scope
- [`docs/benchmarks/renderer-benchmark-summary.md`](../benchmarks/renderer-benchmark-summary.md) — Current benchmark evidence
- [`src/VoxelForge.App/Services/MeshRegionService.cs`](../../src/VoxelForge.App/Services/MeshRegionService.cs) — Current region service (builds full mesh snapshot)
- [`src/VoxelForge.App/Events/ApplicationEvents.cs`](../../src/VoxelForge.App/Events/ApplicationEvents.cs) — `VoxelModelChangedEvent` lacking bounds
- [`src/VoxelForge.Bridge/Handlers/MeshSubscriptionManager.cs`](../../src/VoxelForge.Bridge/Handlers/MeshSubscriptionManager.cs) — Documented no-op handler due to missing event bounds
- [`src/VoxelForge.Core/Meshing/IVoxelMesher.cs`](../../src/VoxelForge.Core/Meshing/IVoxelMesher.cs) — Interface with no bounded overload
- [`src/VoxelForge.Core/Meshing/GreedyMesher.cs`](../../src/VoxelForge.Core/Meshing/GreedyMesher.cs) — Full-model greedy meshing
- [`src/VoxelForge.Engine.MonoGame/Rendering/VoxelRenderer.cs`](../../src/VoxelForge.Engine.MonoGame/Rendering/VoxelRenderer.cs) — Monolithic buffer approach
- [`src/VoxelForge.Engine.MonoGame/EventHandlers.cs`](../../src/VoxelForge.Engine.MonoGame/EventHandlers.cs) — Current dirty handler (marks full renderer dirty)
- [`README.md`](../../README.md) — Project overview
