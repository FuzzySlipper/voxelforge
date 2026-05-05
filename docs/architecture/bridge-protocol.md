# VoxelForge Bridge Protocol

> Task: #1172 | Parent: #1168  
> Status: protocol specification (documentation only)  
> Scope: C#/TS message contract over `den-bridge`; no implementation code.

## Overview

This document defines the **VoxelForge bridge protocol** — a versioned, typed message contract that runs **on top of** `den-bridge` and governs all communication between the TypeScript/Electron renderer process and the C# `VoxelForge.Bridge` sidecar.

The protocol preserves the core architectural boundary documented in [`electron-renderer-experiment.md`](electron-renderer-experiment.md):

- **C# owns** durable editor state, model mutations, command routing, undo/redo, persistence, tool semantics, and mesh generation.
- **TypeScript owns** rendering presentation, UI chrome, camera/pointer presentation state, and raw interaction capture.

Every byte of C#↔TS runtime state synchronization flows through this protocol. There are no secondary sockets, HTTP APIs, file-watchers, or shared-memory channels.

> **Note:** The current smoke-test implementation from task #1171 (`ping` and `version.handshake`) is **not** the full VoxelForge protocol. Those messages are den-bridge-level connectivity probes. The messages defined in this document are VoxelForge-specific and versioned independently.

---

## Protocol Layers

```text
┌─────────────────────────────────────────┐
│  VoxelForge Bridge Protocol (this doc)  │  ← app-specific commands, events, payloads
├─────────────────────────────────────────┤
│  den-bridge base protocol               │  ← framing, routing, correlation, errors
├─────────────────────────────────────────┤
│  Transport (WebSocket / stdio / socket) │  ← bytes on the wire
└─────────────────────────────────────────┘
```

### den-bridge Base Layer

The upstream `den-bridge` submodule (`lib/den-bridge`) provides:

- **Frame types:** `request`, `response`, `event`, `progress`, `cancel`, `health`, `capabilities`
- **Correlation:** `trace_id`, `causation_id`, `parent_request_id`, `operator_session_id`
- **Error model:** `BridgeError` with `code`, `message`, `category`, `details`, `retryable`, `caused_by`
- **Serialization:** `BridgeJson` with `snake_case_lower` naming policy, null omission, and strict case sensitivity

See:
- `lib/den-bridge/src/Den.Bridge/Protocol/Frames.cs` — base frame records
- `lib/den-bridge/src/Den.Bridge/Protocol/BridgeJson.cs` — serializer conventions
- `lib/den-bridge/src/Den.Bridge/Protocol/BridgeProtocol.cs` — protocol constants

### VoxelForge Protocol Layer

VoxelForge-specific messages travel as **den-bridge request/response/event payloads**. The VoxelForge layer adds:

- App-level command and event namespaces (`voxelforge.*`)
- A VoxelForge **schema version**, distinct from the den-bridge protocol version
- Semantic message categories (lifecycle, state, mesh, commands, diagnostics)
- Ownership annotations on every message: **C#-owned** versus **TS-owned**

---

## Versioning and Compatibility

### Two-Layer Versioning

| Layer | Field | Current Value | Semantics |
|-------|-------|---------------|-----------|
| **Transport** | `protocol_version` | `"1.0"` | den-bridge wire format version. Must match exactly between client and server. |
| **Schema** | `schema_version` | `"voxelforge@1"` | VoxelForge message schema version. Independent of den-bridge version. |

### Compatibility Rules

1. **den-bridge protocol version mismatch** is fatal. The connection is refused during handshake.
2. **VoxelForge schema version mismatch** is negotiated. The sidecar advertises its supported schema versions via the `voxelforge.handshake` response. The TS client decides whether it can operate.
3. **Major schema bumps** (`voxelforge@1` → `voxelforge@2`) may remove fields, change command semantics, or rename events. Clients must reject unknown major versions.
4. **Minor schema bumps** (`voxelforge@1.0` → `voxelforge@1.1`) add optional fields, new commands, or new events. Clients must ignore unknown fields (forward compatibility).

### Handshake Flow

```text
TS  --(den-bridge request: command="version.handshake")-->  C#
TS  <--(den-bridge response: sidecar_protocol_version, app_id, app_version)--  C#

TS  --(den-bridge request: command="voxelforge.handshake")-->  C#
     payload: { client_schema_version: "voxelforge@1", supported_capabilities: [...] }
TS  <--(den-bridge response)--  C#
     payload: {
       sidecar_schema_version: "voxelforge@1",
       supported_capabilities: ["mesh_json", "mesh_binary", "state_delta", "commands"],
       compatible: true,
       schema_bundle_id: "voxelforge-schema-2026-05-05"
     }
```

If `compatible` is `false`, the TS client must surface an error to the user and not attempt further VoxelForge-specific commands.

---

## Wire Format and Serialization Conventions

### Naming Convention

All JSON property names on the wire use **snake_case_lower**. This is enforced by `BridgeJson.SerializerOptions` (`PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower`).

C# DTOs must use `[JsonPropertyName("snake_case")]` attributes. TypeScript clients must construct and read snake_case keys. This convention is **non-negotiable** and prevents silent drift in payload shape between C# and TS.

> **Follow-up note (#1183):** The current smoke-test TS client (`electron/src/main/bridge-client.ts`) constructs frames with snake_case keys inline. Future work should generate TypeScript typings from C# DTOs or JSON Schema so the convention is machine-checked rather than manual.

### Null Handling

Null values are **omitted** from serialized JSON (`JsonIgnoreCondition.WhenWritingNull`). A missing field is equivalent to `null` on the receiving side. This keeps payloads compact and avoids ambiguity between explicit `null` and absence.

### Case Sensitivity

Property name matching is **case-sensitive** (`PropertyNameCaseInsensitive = false`). `"requestId"` does not match `"request_id"`. This catches casing mismatches early.

### Date/Timestamp Format

- Timestamps use ISO 8601 strings in UTC (`DateTimeOffset.UtcNow.ToString("O")`).
- Durations use milliseconds as `long` integers.

---

## Request/Response Correlation

### Request ID

Every request carries a client-generated `request_id` (string, unique within the client session). The response **must** echo the same `request_id`. The TS client rejects responses with unknown `request_id` values.

### Correlation Context

The `correlation` object on every frame supports distributed tracing:

| Field | Owner | Purpose |
|-------|-------|---------|
| `trace_id` | TS | Root trace identifier for an end-to-end user action. |
| `causation_id` | TS | ID of the event/cause that triggered this request. |
| `parent_request_id` | TS | For nested requests (e.g., a command that triggers a mesh rebuild). |
| `operator_session_id` | TS | Human operator session identifier for audit trails. |
| `metadata` | TS | Arbitrary key/value bag for client-specific telemetry. |

C# handlers **must** copy the incoming `correlation` object into the response frame unchanged. C# event frames **should** include the `trace_id` of the request that caused the state change, if applicable.

### Timeout and Cancellation

- TS clients set a `deadline_ms` on requests. C# handlers should respect it and throw `OperationCanceledException` on timeout.
- TS clients may send a `cancel` frame with the `request_id` to abort long-running operations (e.g., large mesh snapshots).
- C# handlers that support cancellation advertise it in their capability metadata (`supports_cancellation: true`).

---

## Error Handling Expectations

### Error Structure

All errors use the den-bridge `BridgeError` record:

```json
{
  "code": "voxelforge.mesh.snapshot_failed",
  "message": "Mesh generation failed for model 'untitled'.",
  "category": "internal",
  "details": { "model_id": "untitled", "vertex_count": 0 },
  "retryable": false,
  "caused_by": []
}
```

### VoxelForge Error Categories

VoxelForge uses the den-bridge category strings plus app-specific semantics:

| Category | Meaning | Example |
|----------|---------|---------|
| `validation` | TS sent malformed or semantically invalid data. | Invalid voxel coordinate, unknown palette index. |
| `not_found` | Referenced entity does not exist. | Requested mesh for a model ID that is not loaded. |
| `conflict` | Operation conflicts with current state. | Attempting to save over a file changed on disk. |
| `transient` | Temporary failure; retry may succeed. | Sidecar is still initializing; mesh service busy. |
| `internal` | C# sidecar bug or unhandled exception. | Mesh generator threw NullReferenceException. |
| `unsupported_capability` | C# sidecar does not support a feature the TS client requested. | Binary mesh payloads not enabled in this build. |
| `cancelled` | Operation was cancelled by client or shutdown. | TS sent `cancel` frame during mesh generation. |

### Retry Guidance

- `retryable: true` means the TS client may retry the **same** request without modification. Exponential back-off is recommended.
- `retryable: false` means retrying the same request will produce the same error. The TS client must surface the error to the user or modify the request.
- `caused_by` chains inner errors for debugging. TS UI should display the outermost message but log the full chain.

### Error Codes

Error codes use dot-namespaced strings:

- `voxelforge.{domain}.{specific}` for VoxelForge-specific errors.
- `bridge.{den-bridge-code}` for transport/framework errors (re-used from den-bridge).

Example domains: `lifecycle`, `state`, `mesh`, `command`, `tool`, `project`, `session`, `diagnostics`.

---

## Message Ownership

Every message in this protocol is tagged with an **owner**:

- **C#-owned:** The message carries authoritative semantic state, validation logic, or durable truth. TS must treat the payload as read-only and must not infer model logic from it.
- **TS-owned:** The message carries presentation-state, raw interaction data, or display parameters. C# must treat the payload as advisory and must not treat TS coordinates or camera angles as authoritative model mutations without validation.

> **Boundary rule:** TS must never send a message that directly mutates `VoxelModel`, `LabelIndex`, `UndoHistoryState`, or `EditorDocumentState`. TS sends **intents** ("I want to place a voxel here"); C# validates, applies, and returns **authoritative snapshots**.

---

## Message Reference

### Lifecycle

#### `voxelforge.handshake` (TS → C#, request/response)

**TS-owned request.** Sent after den-bridge transport handshake completes. Advertises what the TS client supports.

Request payload:
```json
{
  "client_schema_version": "voxelforge@1",
  "supported_capabilities": ["mesh_json", "state_delta", "commands"]
}
```

**C#-owned response.** Declares the sidecar's authoritative schema version and capability set.

Response payload:
```json
{
  "sidecar_schema_version": "voxelforge@1",
  "supported_capabilities": ["mesh_json", "mesh_binary", "state_delta", "commands", "progress"],
  "compatible": true,
  "schema_bundle_id": "voxelforge-schema-2026-05-05"
}
```

- `schema_bundle_id` is an opaque string that identifies the exact schema bundle the sidecar was built with. Useful for debugging version skew.

---

#### `voxelforge.health.subscribe` / `voxelforge.health.unsubscribe` (TS → C#, request/response)

**TS-owned request.** Asks the sidecar to start/stop sending periodic `health` event frames (den-bridge built-in `BridgeHealthFrame`).

The sidecar re-uses the den-bridge `health` frame type with VoxelForge-specific `app_id` and `degraded_subsystems` values.

---

### Editor State Snapshots and Deltas

State messages are **C#-owned**. The TS renderer is an observer; it does not own, cache, or reconstruct authoritative state from deltas.

#### `voxelforge.state.subscribe` (TS → C#, request/response)

**TS-owned request.** Subscribes to state change events for one or more state domains.

Request payload:
```json
{
  "domains": ["document", "session", "config", "history"],
  "delivery_mode": "delta",
  "full_snapshot_on_subscribe": true
}
```

- `domains`: Which state objects to observe (see [State Boundaries](state-boundaries.md)).
- `delivery_mode`:
  - `"delta"` — C# pushes only changed fields (preferred for performance).
  - `"snapshot"` — C# pushes the full state object on every change (simpler, higher bandwidth).
- `full_snapshot_on_subscribe`: If `true`, the sidecar immediately sends the current full state for all requested domains.

**C#-owned response.** Acknowledges subscription and returns the current snapshot if requested.

---

#### `voxelforge.state.unsubscribe` (TS → C#, request/response)

**TS-owned request.** Cancels a prior subscription.

---

#### `voxelforge.state.delta` (C# → TS, event)

**C#-owned event.** Pushed when a subscribed state domain changes. Carries the authoritative delta.

Event payload:
```json
{
  "domain": "session",
  "sequence": 42,
  "timestamp": "2026-05-05T12:34:56.789Z",
  "full": false,
  "delta": {
    "active_tool": "place",
    "active_palette_index": 3
  },
  "snapshot": null
}
```

- `sequence`: Monotonically increasing per-domain sequence number. TS clients can detect gaps and request a full re-sync.
- `full`: If `true`, `snapshot` contains the complete state object and `delta` is omitted.
- `delta`: Partial object with only changed fields. TS merges this into its local presentation copy.
- `snapshot`: Complete state object when `full: true` or when the sidecar decides a delta is too complex.

**State domains and their C#-owned fields:**

| Domain | C#-owned fields (authoritative) | TS-owned fields (presentation only, never sent in delta) |
|--------|--------------------------------|----------------------------------------------------------|
| `document` | `model_id`, `voxel_count`, `palette`, `labels`, `animation_clips`, `dirty` | — |
| `session` | `active_tool`, `active_palette_index`, `selected_voxels`, `selected_region`, `active_frame` | — |
| `config` | `grid_enabled`, `grid_size`, `theme`, `key_bindings` | — |
| `history` | `can_undo`, `can_redo`, `undo_depth`, `last_command_name` | — |

> **Important:** The `document` domain delta contains **metadata only** (voxel count, bounds, palette entries). It does **not** contain the full voxel dictionary. The TS renderer receives voxel geometry via **mesh snapshots**, not state deltas.

---

#### `voxelforge.state.request_full` (TS → C#, request/response)

**TS-owned request.** Explicitly requests a full snapshot of one or more domains, ignoring the current delivery mode. Used for re-sync after a gap is detected.

---

### Command Requests and Results

Commands are the primary mutation path. TS sends command requests; C# validates, applies via App services, and returns results.

#### `voxelforge.command.execute` (TS → C#, request/response)

**TS-owned request.** Executes a named editor command with typed arguments.

Request payload:
```json
{
  "command_name": "place_voxel",
  "arguments": {
    "position": {"x": 1, "y": 2, "z": 3},
    "palette_index": 3
  },
  "correlation_context": {
    "trace_id": "abc-123",
    "causation_id": "mouse-click-42"
  }
}
```

**C#-owned response.** Returns the command result, emitted events, and updated state hints.

Response payload:
```json
{
  "success": true,
  "message": "Placed voxel at (1, 2, 3).",
  "affected_domains": ["document", "session", "history"],
  "events": [
    {"event": "voxel_model_changed", "affected_bounds": {"min": {"x":1,"y":2,"z":3}, "max": {"x":1,"y":2,"z":3}}}
  ]
}
```

- `success`: Boolean indicating whether the command was applied.
- `message`: Human-readable result string suitable for status bar display.
- `affected_domains`: Hints for which state domains the TS client should expect delta events for.
- `events`: Typed events that the command emitted. TS may use these for optimistic UI updates before the formal delta arrives.

**Command validation rules (C#-owned):**

- C# validates all arguments against current state (e.g., palette index must be in range).
- C# rejects commands that would violate tool semantics (e.g., `remove_voxel` when the active tool is `paint`).
- C# applies the command through the undoable `IEditorCommand` pipeline.
- The response is sent **after** the command is applied and events are published.

---

### Mesh Snapshots and Updates

Mesh messages are **C#-owned**. The TS renderer receives renderer-neutral mesh data and converts it to Three.js objects. TS does not generate, validate, or modify mesh topology.

#### `voxelforge.mesh.request_snapshot` (TS → C#, request/response)

**TS-owned request.** Requests a complete mesh snapshot for the current (or specified) model.

Request payload:
```json
{
  "model_id": "untitled",
  "lod_level": 0,
  "payload_format": "json",
  "include_palette_mapping": true
}
```

- `model_id`: `""` or omitted means the current active model.
- `lod_level`: Level-of-detail hint. `0` is full detail. Future schema versions may define LOD rules.
- `payload_format`: `"json"` or `"binary"`. See [Mesh Payload Strategy](#mesh-payload-strategy).
- `include_palette_mapping`: If `true`, the response includes the palette index → material ID mapping.

**C#-owned response.** Returns mesh data.

Response payload (JSON format):
```json
{
  "model_id": "untitled",
  "mesh_id": "mesh-untitled-20260505-123456",
  "format": "json",
  "vertex_count": 1024,
  "index_count": 3072,
  "bounds": {
    "min": {"x": 0, "y": 0, "z": 0},
    "max": {"x": 10, "y": 10, "z": 10}
  },
  "vertices": [0.0, 1.0, 2.0, ...],
  "indices": [0, 1, 2, ...],
  "normals": [0.0, 0.0, 1.0, ...],
  "colors": [255, 0, 0, 255, ...],
  "palette_indices": [3, 3, 3, ...],
  "palette_mapping": {
    "3": {"name": "Red", "color": "#FF0000", "material_id": "mat-red"}
  }
}
```

- `mesh_id`: Opaque identifier for this mesh generation result. TS may use this for caching.
- `vertices`: Flat array of `float32` XYZ coordinates.
- `indices`: Flat array of `uint32` indices into the vertex array.
- `normals`: Flat array of `float32` XYZ normals (optional; sidecar may omit if TS should compute flat normals).
- `colors`: Flat array of `uint8` RGBA values per vertex (optional; preferred when palette mapping is not used).
- `palette_indices`: One index per face or per vertex indicating which palette entry applies (optional; C# decides granularity).

---

#### `voxelforge.mesh.update` (C# → TS, event)

**C#-owned event.** Pushed when a mesh changes incrementally (e.g., after a voxel edit). Sent only when the TS client has an active mesh subscription.

Event payload:
```json
{
  "model_id": "untitled",
  "base_mesh_id": "mesh-untitled-20260505-123456",
  "update_type": "incremental",
  "changed_regions": [
    {
      "bounds": {"min": {"x":1,"y":2,"z":3}, "max": {"x":1,"y":2,"z":3}},
      "vertex_offset": 1024,
      "vertex_count": 24,
      "index_offset": 3072,
      "index_count": 36
    }
  ],
  "payload_format": "json",
  "vertices": [ ... ],
  "indices": [ ... ]
}
```

- `update_type`: `"incremental"` (patch existing mesh) or `"full_replace"` (drop and rebuild).
- `changed_regions`: Spatial bounds and buffer offsets for the changed geometry.

> **Task reference:** Full incremental mesh update pipeline is task #1176. This schema reserves the shape; initial vertical slice (#1177) may use `full_replace` for all updates.

---

#### `voxelforge.mesh.subscribe` / `voxelforge.mesh.unsubscribe` (TS → C#, request/response)

**TS-owned request.** Subscribe/unsubscribe to mesh update events for a model. After subscribing, the sidecar pushes `voxelforge.mesh.update` events whenever the model's mesh changes.

---

### Palette and Materials

Palette messages are **C#-owned** for semantic content and **TS-owned** for presentation overrides.

#### `voxelforge.palette.get` (TS → C#, request/response)

**TS-owned request.** Requests the current palette definition.

**C#-owned response.** Returns the authoritative palette.

Response payload:
```json
{
  "palette_id": "default",
  "entries": [
    {"index": 0, "name": "Air", "color": "#000000", "visible": false},
    {"index": 1, "name": "Stone", "color": "#808080", "visible": true},
    {"index": 3, "name": "Red", "color": "#FF0000", "visible": true}
  ]
}
```

- `index`: The palette index used in `VoxelModel` and mesh `palette_indices`.
- `name`, `color`: C#-owned semantic definition.
- `visible`: C#-owned visibility flag (e.g., air is invisible).

#### `voxelforge.palette.set_entry` (TS → C#, request/response)

**TS-owned request.** Asks C# to update a palette entry. C# validates and applies.

Request payload:
```json
{
  "index": 3,
  "name": "Crimson",
  "color": "#DC143C"
}
```

C# responds with `success` and pushes a `document` state delta with the updated palette.

> **Boundary note:** TS may offer a color picker UI, but the authoritative palette mutation goes through C#. TS must not maintain a shadow palette and assume C# will match it.

#### `voxelforge.material.get_overrides` / `voxelforge.material.set_override` (TS → C#, request/response)

**TS-owned request.** Gets or sets TS-side material presentation overrides.

Request payload:
```json
{
  "palette_index": 3,
  "material_override": {
    "roughness": 0.5,
    "metalness": 0.0,
    "emissive": "#000000"
  }
}
```

**C# stores but does not interpret** material override values. They are passed through to TS in state deltas so that overrides persist across sessions, but C# does not use them for mesh generation or FNA rendering. This is a **presentation concern owned by TS**.

---

### Selection

Selection messages are **C#-owned** for semantic selection state and **TS-owned** for interaction intents.

#### `voxelforge.selection.set` (TS → C#, request/response)

**TS-owned request.** Sends a selection intent (e.g., "select voxels at these coordinates").

Request payload:
```json
{
  "mode": "replace",
  "voxels": [{"x": 1, "y": 2, "z": 3}],
  "region": null
}
```

- `mode`: `"replace"`, `"add"`, `"remove"`, `"toggle"`, `"clear"`.
- `voxels`: Explicit voxel coordinates.
- `region`: Optional region label name (e.g., `"head"`) for label-based selection.

C# validates the intent against current tool and state, applies the selection change, and pushes a `session` state delta with the authoritative `selected_voxels` and `selected_region`.

#### `voxelforge.selection.get` (TS → C#, request/response)

**C#-owned response.** Returns the current authoritative selection.

Response payload:
```json
{
  "voxels": [{"x": 1, "y": 2, "z": 3}],
  "region": null,
  "bounds": {"min": {"x":1,"y":2,"z":3}, "max": {"x":1,"y":2,"z":3}}
}
```

---

### Camera and View Hints

Camera messages are **TS-owned** for presentation state. C# may read camera hints for features like "render preview from this angle" but does not own the camera.

#### `voxelforge.camera.set` (TS → C#, request/response)

**TS-owned request.** Updates the TS camera state. C# may store this for persistence or preview generation, but it is not authoritative for the renderer.

Request payload:
```json
{
  "position": {"x": 10.0, "y": 10.0, "z": 10.0},
  "target": {"x": 0.0, "y": 0.0, "z": 0.0},
  "up": {"x": 0.0, "y": 1.0, "z": 0.0},
  "fov_degrees": 45.0,
  "projection": "perspective",
  "near_clip": 0.1,
  "far_clip": 1000.0
}
```

#### `voxelforge.camera.get` (TS → C#, request/response)

**C#-owned response.** Returns the last known camera state (or default if never set).

---

#### `voxelforge.view.set_hint` (TS → C#, request/response)

**TS-owned request.** Sets a view hint that C# may use for tool behavior or export.

Request payload:
```json
{
  "hint_type": "snap_to_axis",
  "axis": "y",
  "value": null
}
```

Examples:
- `snap_to_axis`: C# tools may constrain placement to a plane.
- `show_reference_image`: C# may include reference image visibility in saved project state.
- `grid_plane`: Which plane the TS grid is drawn on (`"xy"`, `"xz"`, `"yz"`).

---

### Diagnostics and Errors

#### `voxelforge.diagnostics.subscribe` / `unsubscribe` (TS → C#, request/response)

**TS-owned request.** Subscribe/unsubscribe to diagnostic events.

---

#### `voxelforge.diagnostics.event` (C# → TS, event)

**C#-owned event.** Pushed for non-fatal diagnostic information.

Event payload:
```json
{
  "level": "warning",
  "source": "mesh_service",
  "message": "Mesh generation took 245ms for 500k voxels.",
  "metric": {
    "name": "mesh_generation_ms",
    "value": 245,
    "unit": "ms"
  }
}
```

- `level`: `"debug"`, `"info"`, `"warning"`, `"error"`.
- `source`: C# subsystem name.
- `metric`: Optional structured metric for telemetry.

---

#### `voxelforge.diagnostics.error` (C# → TS, event)

**C#-owned event.** Pushed for unrecoverable or user-visible errors that were not tied to a specific request.

Event payload:
```json
{
  "level": "error",
  "source": "project_serializer",
  "message": "Failed to load project: file not found.",
  "error": {
    "code": "voxelforge.project.load_failed",
    "message": "File '/path/to/model.vforge' not found.",
    "category": "not_found",
    "retryable": false
  }
}
```

---

## Mesh Payload Strategy

### When JSON Is Acceptable

JSON mesh payloads (`"payload_format": "json"`) are acceptable when:

- Vertex count is under ~10,000 (roughly 1,000 voxels with greedy meshing).
- The message is a state delta or UI update, not a full scene render.
- Latency matters more than bandwidth (JSON avoids extra framing/negotiation).
- The TS client has not advertised `mesh_binary` capability.

### When Binary / Framed Payload Support Is Needed

Binary mesh payloads (`"payload_format": "binary"`) are preferred when:

- Vertex count exceeds ~10,000.
- The message is a full initial mesh snapshot for a large model.
- Frame-time budget requires minimizing parse/allocate overhead in the TS renderer.
- The mesh includes additional attribute channels (UVs, tangents, bone weights) that bloat JSON.

### Binary Payload Framing

When binary format is requested, the response uses a **two-part delivery**:

1. **JSON control frame** (den-bridge `response` frame) containing metadata:
   ```json
   {
     "model_id": "untitled",
     "mesh_id": "mesh-untitled-20260505-123456",
     "format": "binary",
     "binary_payload_id": "bp-abc123",
     "vertex_count": 50000,
     "index_count": 150000,
     "vertex_stride_bytes": 32,
     "index_size_bytes": 4,
     "bounds": { ... },
     "palette_mapping": { ... }
   }
   ```

2. **Binary payload frame** delivered out-of-band:
   - Over WebSocket: a subsequent binary WebSocket frame tagged with `binary_payload_id`.
   - Over stdio: a length-prefixed binary chunk following the JSON line.

The binary layout is a single interleaved vertex buffer followed by an index buffer:

```text
[vertex_buffer: vertex_stride_bytes * vertex_count]
[index_buffer: index_size_bytes * index_count]
```

Vertex attribute offsets within the stride are declared in the control frame:

```json
{
  "vertex_layout": [
    {"semantic": "position", "offset": 0, "format": "float32x3"},
    {"semantic": "normal", "offset": 12, "format": "float32x3"},
    {"semantic": "color", "offset": 24, "format": "uint8x4"},
    {"semantic": "palette_index", "offset": 28, "format": "uint8"}
  ]
}
```

> **Future work:** The exact binary transport mechanism depends on upstream `den-bridge` transport support. Task #1174 introduces renderer-neutral mesh services; task #1176 adds incremental mesh updates. This protocol reserves the schema shape so that binary transport can be added without breaking the message contract.

---

## Capability Discovery

After handshake, the TS client knows which capabilities the sidecar supports. Capabilities are **coarse-grained feature flags**, not per-command permissions.

| Capability | Meaning |
|------------|---------|
| `mesh_json` | Sidecar can return mesh snapshots as JSON arrays. |
| `mesh_binary` | Sidecar can return mesh snapshots as binary payloads. |
| `state_delta` | Sidecar supports delta-mode state delivery (not just full snapshots). |
| `state_snapshot` | Sidecar supports snapshot-mode state delivery. |
| `commands` | Sidecar accepts `voxelforge.command.execute` requests. |
| `progress` | Sidecar may send `progress` frames for long-running requests. |
| `incremental_mesh` | Sidecar supports `voxelforge.mesh.update` with `update_type: "incremental"`. (Reserved for #1176.) |
| `diagnostics` | Sidecar emits `voxelforge.diagnostics.event` frames. |

TS clients must degrade gracefully when a capability is absent. For example, if `state_delta` is unsupported, the TS client should subscribe with `delivery_mode: "snapshot"`.

---

## Forbidden Patterns

The following patterns are **explicitly forbidden** by this protocol. They violate the C#/TS ownership boundary and must not appear in bridge messages or adapter code.

### TS Must Never

1. **Send raw voxel dictionary mutations.** Do not send `{"voxels": {"1,2,3": 3}}` and expect C# to apply it. Use `voxelforge.command.execute` with `command_name: "place_voxel"`.
2. **Send undo/redo stack manipulations.** Do not send `{"undo_stack": [...]}`. Use `voxelforge.history.undo`.
3. **Send serialized `.vforge` file content.** Do not read or write `.vforge` bytes in TS. Use `voxelforge.project.save` / `load`.
4. **Send tool result computations.** Do not compute flood-fill results or box-selection bounds in TS and send them as commands. Send the interaction intent and let C# compute the result.
5. **Send label region geometry.** Do not define label shapes in TS. Send `voxelforge.command.execute` with `command_name: "label_region"` and let C# own the `LabelIndex`.
6. **Cache authoritative state and skip delta updates.** The TS renderer may keep a local copy of state for performance, but it must apply incoming deltas and must not assume its copy is authoritative.
7. **Send MCP tool calls or console command strings.** MCP and console logic remain C#-only. TS sends bridge commands; C# routes to services.

### C# Must Never

1. **Accept TS camera position as authoritative for model logic.** Camera hints are advisory.
2. **Accept TS material overrides as semantic palette changes.** Material overrides are presentation-only.
3. **Leak FNA/Myra types into bridge payloads.** Bridge DTOs must use renderer-neutral types (`float`, `int`, `string`, arrays).
4. **Send implementation-specific pointers or handles** (e.g., memory addresses, object IDs) across the bridge.

---

## Message Summary Table

| Command / Event | Direction | Owner | Category |
|-----------------|-----------|-------|----------|
| `voxelforge.handshake` | TS → C# | TS | Lifecycle |
| `voxelforge.handshake` | C# → TS | C# | Lifecycle |
| `voxelforge.health.subscribe` | TS → C# | TS | Lifecycle |
| `voxelforge.state.subscribe` | TS → C# | TS | State |
| `voxelforge.state.unsubscribe` | TS → C# | TS | State |
| `voxelforge.state.delta` | C# → TS | C# | State |
| `voxelforge.state.request_full` | TS → C# | TS | State |
| `voxelforge.command.execute` | TS → C# | TS | Commands |
| `voxelforge.command.execute` | C# → TS | C# | Commands |
| `voxelforge.mesh.request_snapshot` | TS → C# | TS | Mesh |
| `voxelforge.mesh.request_snapshot` | C# → TS | C# | Mesh |
| `voxelforge.mesh.update` | C# → TS | C# | Mesh |
| `voxelforge.mesh.subscribe` | TS → C# | TS | Mesh |
| `voxelforge.mesh.unsubscribe` | TS → C# | TS | Mesh |
| `voxelforge.palette.get` | TS → C# | TS | Palette |
| `voxelforge.palette.get` | C# → TS | C# | Palette |
| `voxelforge.palette.set_entry` | TS → C# | TS | Palette |
| `voxelforge.material.get_overrides` | TS → C# | TS | Materials |
| `voxelforge.material.set_override` | TS → C# | TS | Materials |
| `voxelforge.selection.set` | TS → C# | TS | Selection |
| `voxelforge.selection.get` | TS → C# | TS | Selection |
| `voxelforge.selection.get` | C# → TS | C# | Selection |
| `voxelforge.camera.set` | TS → C# | TS | Camera |
| `voxelforge.camera.get` | TS → C# | TS | Camera |
| `voxelforge.camera.get` | C# → TS | C# | Camera |
| `voxelforge.view.set_hint` | TS → C# | TS | Camera |
| `voxelforge.diagnostics.subscribe` | TS → C# | TS | Diagnostics |
| `voxelforge.diagnostics.event` | C# → TS | C# | Diagnostics |
| `voxelforge.diagnostics.error` | C# → TS | C# | Diagnostics |

---

## Cross-References

- [`electron-renderer-experiment.md`](electron-renderer-experiment.md) — Architecture plan with boundary rules, repo layout, and success metrics.
- [`events-states-services.md`](events-states-services.md) — Adapter/service rules that apply to bridge handlers.
- [`state-boundaries.md`](state-boundaries.md) — State ownership map that C# retains and TS observes.
- `src/VoxelForge.Bridge/Protocol/SmokeTestMessages.cs` — Current minimal smoke-test messages (not part of this protocol).
- `electron/src/main/bridge-client.ts` — Current minimal TS smoke client.
- `lib/den-bridge/src/Den.Bridge/Protocol/Frames.cs` — den-bridge base frame types.
- `lib/den-bridge/src/Den.Bridge/Protocol/BridgeJson.cs` — den-bridge JSON serializer conventions.
- `lib/den-bridge/src/Den.Bridge/Protocol/BridgeProtocol.cs` — den-bridge protocol and error constants.

---

## Future Work (Out of Scope for #1172)

- **Binary transport implementation:** Depends on upstream `den-bridge` transport hardening (#1173).
- **Renderer-neutral mesh services:** Task #1174 defines C# mesh generation APIs that this protocol will call.
- **Incremental mesh updates:** Task #1176 implements the `voxelforge.mesh.update` incremental delivery path.
- **JSON Schema / TypeScript typings generation:** Task #1183 covers generating typed bindings from C# DTOs to prevent manual snake_case drift.
- **Protocol round-trip tests:** Task #1177 vertical slice will exercise the full request/response/event flow.
