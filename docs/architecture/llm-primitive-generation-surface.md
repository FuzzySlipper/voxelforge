# LLM Primitive Generation Surface

Task 752 defines the first high-level voxel generation surface for LLM-driven authoring. The goal is to let a model describe common geometry with compact primitives instead of emitting every voxel coordinate through `set_voxels`.

This is a design document. It fixes the API shape and implementation path so follow-up tasks can implement the feature without re-deciding boundaries.

## Goals

- Add a token-efficient LLM tool for larger voxel edits: blocks, boxes, and lines.
- Keep the surface low-level, deterministic, and easy for models to reason about.
- Preserve the existing Events/States/Services architecture.
- Route every resulting model mutation through undoable editor commands.
- Keep Core free of App, Engine, FNA, Myra, transport SDKs, and UI types.

## Non-goals

- No mesh, material, renderer, camera, or screenshot behavior.
- No procedural natural-language prompt interpretation inside VoxelForge.
- No automatic semantic region creation in the first implementation. Region labeling remains a separate tool/service path.
- No destructive boolean modeling beyond overwriting target voxels with a palette index.

## Tool name

Primary LLM tool:

```text
apply_voxel_primitives
```

One call accepts a batch of primitive operations. Batching keeps LLM output compact and gives users one undo step for the whole generated structure.

The existing `set_voxels` and `remove_voxels` tools remain the precise edit surface. `apply_voxel_primitives` is an additive convenience layer over the same mutation/undo path.

## JSON argument schema

The handler exposes this schema through `ToolDefinition.ParametersSchema`:

```json
{
  "type": "object",
  "properties": {
    "primitives": {
      "type": "array",
      "minItems": 1,
      "items": {
        "type": "object",
        "properties": {
          "id": {
            "type": "string",
            "description": "Optional caller label for diagnostics only."
          },
          "kind": {
            "type": "string",
            "enum": ["block", "box", "line"]
          },
          "palette_index": {
            "type": "integer",
            "minimum": 1,
            "maximum": 255
          },
          "at": { "$ref": "#/$defs/point" },
          "from": { "$ref": "#/$defs/point" },
          "to": { "$ref": "#/$defs/point" },
          "mode": {
            "type": "string",
            "enum": ["filled", "shell", "edges"],
            "description": "Box fill mode. Defaults to filled. Ignored for block and line."
          },
          "radius": {
            "type": "integer",
            "minimum": 0,
            "maximum": 16,
            "description": "Line brush radius in Chebyshev voxel distance. Defaults to 0."
          }
        },
        "required": ["kind", "palette_index"]
      }
    },
    "max_generated_voxels": {
      "type": "integer",
      "minimum": 1,
      "maximum": 65536,
      "description": "Safety cap after de-duplication. Defaults to 8192."
    },
    "preview_only": {
      "type": "boolean",
      "description": "If true, validate and report generated counts without mutating. Defaults to false."
    }
  },
  "required": ["primitives"],
  "$defs": {
    "point": {
      "type": "object",
      "properties": {
        "x": { "type": "integer" },
        "y": { "type": "integer" },
        "z": { "type": "integer" }
      },
      "required": ["x", "y", "z"]
    }
  }
}
```

The implementation may inline the point definition for transports that have limited `$ref` support. The logical schema above remains the API contract.

## Primitive semantics

All coordinates are integer voxel coordinates. Corners and endpoints are inclusive.

### `block`

Required fields:

- `kind: "block"`
- `at`
- `palette_index`

Effect: set exactly one voxel at `at` to `palette_index`.

Equivalent precise edit:

```json
{ "voxels": [{ "x": at.x, "y": at.y, "z": at.z, "i": palette_index }] }
```

### `box`

Required fields:

- `kind: "box"`
- `from`
- `to`
- `palette_index`

Optional fields:

- `mode`: `filled` by default; `shell` and `edges` are supported.

Corner order does not matter. The service normalizes `from`/`to` to min/max bounds.

Modes:

- `filled`: every voxel in the inclusive cuboid.
- `shell`: voxels where at least one coordinate is on the min/max face.
- `edges`: voxels where at least two coordinates are on min/max faces.

For degenerate boxes, the definitions naturally collapse:

- A 1-voxel box produces one voxel for every mode.
- A flat rectangle has `shell == filled` because every voxel lies on at least one face.
- A line-shaped box has `edges == filled` because every voxel lies on at least two boundary axes.

### `line`

Required fields:

- `kind: "line"`
- `from`
- `to`
- `palette_index`

Optional fields:

- `radius`: default `0`, maximum `16`.

Line rasterization is deterministic integer DDA:

1. `dx = to.x - from.x`, `dy = to.y - from.y`, `dz = to.z - from.z`.
2. `steps = max(abs(dx), abs(dy), abs(dz))`.
3. If `steps == 0`, the path contains only `from`.
4. For each integer `i` from `0` to `steps`, compute:
   - `x = from.x + round_away_from_zero(dx * i / steps)`
   - `y = from.y + round_away_from_zero(dy * i / steps)`
   - `z = from.z + round_away_from_zero(dz * i / steps)`
5. De-duplicate repeated path points while preserving first-seen path order.

A positive `radius` expands every path point with a filled Chebyshev cube brush:

```text
abs(px - cx) <= radius && abs(py - cy) <= radius && abs(pz - cz) <= radius
```

Brush-expanded points are de-duplicated after all primitives are generated.

## Batch conflict semantics

Primitive operations are evaluated in the array order supplied by the caller.

If multiple primitives generate the same coordinate, the later primitive wins. The final mutation intent contains one assignment per coordinate with the winning palette index.

The handler reports:

- primitive count
- generated voxel count before de-duplication
- final unique voxel count
- per-primitive generated counts
- normalized bounds for every primitive

No partial mutation is allowed. Any validation error rejects the whole tool call and returns no mutation intent.

## Safety limits

The Core service validates before returning a mutation intent.

- `palette_index` must be `1..255`; index `0` remains air/reserved.
- `max_generated_voxels` defaults to `8192` and cannot exceed `65536` in the first implementation.
- The safety cap applies after de-duplication, because that is the number of undoable assignments that will be applied.
- A primitive with malformed coordinates, unsupported `kind`, unsupported `mode`, or oversized `radius` rejects the entire request.
- `preview_only: true` returns the same counts and bounds but no `VoxelMutationIntent`.

## Core types

Add these Core-only types under `VoxelForge.Core.Services`:

```csharp
public enum VoxelPrimitiveKind { Block, Box, Line }
public enum VoxelBoxMode { Filled, Shell, Edges }

public readonly record struct VoxelPrimitivePoint(int X, int Y, int Z);

public sealed class VoxelPrimitiveRequest
{
    public string? Id { get; init; }
    public required VoxelPrimitiveKind Kind { get; init; }
    public required int PaletteIndex { get; init; }
    public VoxelPrimitivePoint? At { get; init; }
    public VoxelPrimitivePoint? From { get; init; }
    public VoxelPrimitivePoint? To { get; init; }
    public VoxelBoxMode Mode { get; init; } = VoxelBoxMode.Filled;
    public int Radius { get; init; }
}

public sealed class ApplyVoxelPrimitivesRequest
{
    public required IReadOnlyList<VoxelPrimitiveRequest> Primitives { get; init; }
    public int MaxGeneratedVoxels { get; init; } = 8192;
    public bool PreviewOnly { get; init; }
}

public sealed class VoxelPrimitiveGenerationResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public VoxelMutationIntent? Intent { get; init; }
    public required IReadOnlyList<VoxelPrimitiveSummary> Summaries { get; init; }
}
```

`VoxelPrimitiveGenerationService` is stateless. It owns validation, geometry expansion, de-duplication, and conversion to the existing `VoxelMutationIntent` type.

Core stays transport-agnostic: no JSON SDK behavior outside LLM handlers, no App commands, no Engine/UI dependencies.

## LLM handler

Add a named handler:

```csharp
public sealed class ApplyVoxelPrimitivesHandler : IToolHandler
```

Responsibilities:

1. Expose the `apply_voxel_primitives` schema via `GetDefinition()`.
2. Parse JSON arguments into `ApplyVoxelPrimitivesRequest`.
3. Call `VoxelPrimitiveGenerationService.BuildIntent(request)`.
4. Return `ToolHandlerResult` with:
   - summary text for the model
   - `IsError = true` on validation failure
   - `MutationIntent = result.Intent` when not preview-only

The handler must not mutate `VoxelModel` directly. It also does not need `LabelIndex` or animation clips except to satisfy the existing `IToolHandler` signature.

## Lowering into undoable editor commands

The first implementation should use the existing LLM mutation path:

```text
LLM tool call
  -> ApplyVoxelPrimitivesHandler
  -> VoxelPrimitiveGenerationService.BuildIntent(...)
  -> ToolHandlerResult.MutationIntent
  -> LlmToolApplicationService.ApplyMutationIntents(...)
  -> VoxelEditingService.ApplyMutationIntent(...)
  -> UndoStack.Execute(new CompoundCommand([... SetVoxelCommand ...]))
  -> VoxelModelChangedEvent
```

This path preserves all current undo semantics:

- The entire primitive batch becomes one undo stack entry.
- Each generated coordinate is applied through `SetVoxelCommand` inside a `CompoundCommand`.
- Undo restores the old value for every affected coordinate.
- Renderer dirtying and external adapters observe the existing `VoxelModelChangedEvent` path.

The design intentionally reuses `VoxelMutationIntent` instead of adding a second intent type for primitives. Primitive operations are a more compact tool input format; after validation they are ordinary voxel assignments.

### Future optimization without API changes

If large filled boxes become slow, App can add an internal `VoxelPrimitiveApplicationService` or `SetVoxelBatchCommand` that applies the same Core request more efficiently. That optimization must keep the public `apply_voxel_primitives` schema unchanged and still execute through `UndoStack`.

## Registration path

Registration remains explicit.

### Core / LLM

- Add `VoxelPrimitiveGenerationService` as a normal constructed dependency.
- Add `ApplyVoxelPrimitivesHandler` under `src/VoxelForge.Core/LLM/Handlers/`.
- Wherever a `ToolLoop` is composed, append `new ApplyVoxelPrimitivesHandler(new VoxelPrimitiveGenerationService())` to the explicit handler list.

If a shared registry is introduced later, name it `LlmToolHandlerRegistry` to avoid collision with the editor `ToolRegistry`, and keep it an explicit list of named handler constructions, not reflection scanning.

### MCP adapter

MCP can expose the same handler through the existing `LlmToolMcpTool` adapter:

```csharp
public sealed class ApplyVoxelPrimitivesMcpTool : LlmToolMcpTool
{
    public ApplyVoxelPrimitivesMcpTool(
        ApplyVoxelPrimitivesHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: false)
    {
    }
}
```

Then explicitly register:

- `VoxelPrimitiveGenerationService`
- `ApplyVoxelPrimitivesHandler`
- `ApplyVoxelPrimitivesMcpTool`
- `ApplyVoxelPrimitivesServerTool`

in `VoxelForgeMcpToolRegistry.AddVoxelForgeMcpTools`.

### Console / stdio

The existing stdio JSON-line console protocol should not be used as the primary implementation path for this surface. If a CLI command is desired, add a thin named console command that parses already-tokenized arguments and delegates to the same Core service plus `VoxelEditingService.ApplyMutationIntent`.

## Examples

### Filled box

```json
{
  "primitives": [
    {
      "id": "bracket_body",
      "kind": "box",
      "from": { "x": 0, "y": 0, "z": 0 },
      "to": { "x": 7, "y": 2, "z": 3 },
      "palette_index": 2
    }
  ]
}
```

### Hollow-ish shell and rail line

```json
{
  "max_generated_voxels": 12000,
  "primitives": [
    {
      "id": "outer_shell",
      "kind": "box",
      "from": { "x": 0, "y": 0, "z": 0 },
      "to": { "x": 15, "y": 8, "z": 6 },
      "mode": "shell",
      "palette_index": 4
    },
    {
      "id": "diagonal_strut",
      "kind": "line",
      "from": { "x": 0, "y": 0, "z": 0 },
      "to": { "x": 15, "y": 8, "z": 6 },
      "radius": 1,
      "palette_index": 5
    }
  ]
}
```

### Preview

```json
{
  "preview_only": true,
  "primitives": [
    {
      "kind": "box",
      "from": { "x": -4, "y": 0, "z": -4 },
      "to": { "x": 4, "y": 1, "z": 4 },
      "palette_index": 3
    }
  ]
}
```

Preview responses include counts and bounds but no undo entry.

## Tests expected in follow-up implementation

- Core service tests:
  - `block` generates one assignment.
  - `box` normalizes reversed corners.
  - `filled`, `shell`, and `edges` counts are correct for 3D and degenerate boxes.
  - `line` DDA includes endpoints, de-duplicates points, and expands `radius` deterministically.
  - later primitives override earlier primitives for duplicate coordinates.
  - invalid palette index, invalid mode/kind, oversized radius, and safety cap errors return no intent.
- LLM tests:
  - `ApplyVoxelPrimitivesHandler` returns a `VoxelMutationIntent` for normal calls.
  - `preview_only` returns no mutation intent.
  - `ToolLoop` collects the primitive mutation intent like existing `set_voxels`.
- App tests:
  - applying a primitive-generated mutation intent creates one undoable compound entry and undo restores old voxel values.
- MCP tests:
  - explicit registry includes `apply_voxel_primitives`.
  - calling it through `LlmToolMcpTool` mutates session state through undoable App services.

## Follow-up implementation split

1. **Core primitive service** — add `VoxelPrimitiveGenerationService`, request/result DTOs, and Core unit tests for geometry/counts/validation.
2. **LLM handler** — add `ApplyVoxelPrimitivesHandler`, schema parsing, and ToolLoop tests.
3. **App application coverage** — add focused tests proving primitive intents undo/redo through `VoxelEditingService.ApplyMutationIntent`.
4. **MCP exposure** — add MCP adapter wrapper, explicit registry entries, docs, and MCP tests.
5. **Optional console command** — add a CLI/stdin compatibility wrapper only if users need primitive generation from the text console.

These follow-ups can be implemented independently without changing the public `apply_voxel_primitives` schema.
