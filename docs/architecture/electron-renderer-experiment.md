# Electron Renderer Experiment Architecture Plan

> Task: #1169 | Parent: #1168
> Status: architecture plan (documentation only)

## Overview

This document defines the architecture for an Electron-based renderer experiment for VoxelForge. The experiment evaluates whether a TypeScript/Electron frontend with a Three.js renderer can replace or supplement the current FNA/Myra frontend while preserving all existing VoxelForge architecture rules.

During the experiment, the existing FNA/Myra frontend remains the supported control path. The Electron renderer is an additive experiment, not a migration.

## Core Principle: C# Owns Truth, TypeScript Owns Pixels

The absolute boundary is that **C# owns all durable editor state, model mutations, undo/redo, persistence, tool semantics, command routing, and MCP/console logic**. TypeScript owns **rendering, UI presentation, camera/panel/pointer presentation state, and raw interaction capture**. Every crossing between the two processes flows through `den-bridge`.

This preserves the existing VoxelForge Events-States-Services architecture ([`events-states-services.md`](events-states-services.md)) and state boundaries ([`state-boundaries.md`](state-boundaries.md)). The Electron/TS process is conceptually a new adapter over the same application services, not a replacement for them.

---

## Process Boundaries

### Processes

| Process | Runtime | Responsibilities |
|---------|---------|------------------|
| **Electron main** | Node.js | Window management, native OS integration, lifecycle, spawn C# sidecar, own `den-bridge` client connection |
| **Electron renderer** | Chromium + TS | Three.js rendering, UI panels, camera/pointer state, raw input capture, bridge message routing to main |
| **C# sidecar** | .NET 10 | VoxelForge App/Core services, state, commands, undo/redo, meshing, persistence, MCP/console adapters |

### Process Boundary Rules

1. **No shared memory.** The C# sidecar and Electron processes are separate OS processes. All communication is message-based over `den-bridge`.
2. **No direct file-system coordination for state sync.** The sidecar may write `.vforge` snapshots for persistence, but the Electron renderer must not read them to discover state changes. State changes travel through the bridge.
3. **No HTTP/MCP leakage into the renderer.** The existing `VoxelForge.Mcp` HTTP server ([`docs/mcp-server.md`](../mcp-server.md)) remains a headless C# adapter. The Electron renderer does not call MCP tools directly; it routes editor interactions through the bridge to the sidecar, which applies them via services.
4. **Renderer is an observer with intent.** The renderer observes model/mesh/camera state pushed from C# and sends interaction intents (e.g., "place voxel at (x,y,z) with palette index 3") back to C#. The renderer does not apply the mutation itself.

---

## Allowed TypeScript Responsibilities

TypeScript in the Electron renderer process may own:

### Rendering
- Three.js scene graph construction and updates.
- Voxel mesh display from renderer-neutral mesh snapshots provided by C#.
- Camera framing, orbit, zoom, and snap-to-view controls.
- Wireframe toggle, grid display, and visual helpers.
- Animation playback display (frame-swap timing driven by C# state, visualized by TS).
- Reference image/model overlay rendering if applicable.

### UI Presentation
- Panel chrome, tool palettes, property editors, and status bars.
- Theme, layout, and responsive widget behavior.
- Toast and status message display (content supplied by C# events).

### Camera / Panel / Pointer Presentation State
- Current camera position, rotation, zoom level, and projection mode (perspective/orthographic).
- Which panel is focused or visible.
- Pointer/hover state over the 3D canvas (which voxel coordinate is hovered, which face is targeted).
- Selection highlight presentation (which voxels are drawn as selected, based on C# `EditorSessionState`).

### Raw Interaction Capture
- Mouse/keyboard/touch event capture in the renderer window.
- Mapping raw input to semantic interaction intents (e.g., "drag started at screen (u,v) over voxel (x,y,z)").
- Sending those intents to the C# sidecar through `den-bridge`.

---

## Forbidden TypeScript Responsibilities

TypeScript must **never**:

### Voxel / Editor Mutations
- Modify `VoxelModel` data directly.
- Apply palette changes, label assignments, region edits, or animation clip modifications.
- Perform spatial queries such as flood-fill, box selection expansion, or collision detection.

### Undo / Redo
- Maintain an undo stack.
- Implement undo/redo logic or command replay.
- Decide what is undoable.

### Persistence
- Read or write `.vforge` files directly (except where C# sidecar explicitly delegates file-picker UI).
- Manage save/load lifecycle or dirty state.
- Serialize or deserialize model data.

### Semantic Tool Behavior
- Implement tool semantics such as Place, Remove, Paint, Select, Fill, or Label logic.
- Interpret tool options or validation rules.
- Compute tool results (e.g., which voxels a fill box would affect).

### Command Routing
- Route console-like commands or LLM tool calls.
- Implement `CommandRouter`, `IConsoleCommand`, or `IToolHandler` behavior.
- Dispatch MCP tool requests.

### MCP / Console Logic
- Host or call MCP endpoints.
- Implement stdio JSON-line handling.
- Execute console commands directly.

### Model Ownership
- Own the authoritative copy of `EditorDocumentState`, `EditorSessionState`, `UndoHistoryState`, `EditorConfigState`, or any derived index.
- Rebuild `LabelIndex`, mesh caches, or spatial acceleration structures from raw voxel data.
- Decide when the renderer is dirty; dirty signaling is driven by C# typed events.

---

## den-bridge: The Only C#/TS Communication Path

### Design Rule

Every byte of C#↔TS communication during the experiment flows through `den-bridge`. There are no secondary sockets, HTTP APIs, file-watchers, or shared-memory channels for runtime state synchronization.

### What den-bridge Provides

`den-bridge` is shared upstream infrastructure (a separate repository consumed as a git submodule). It provides:

- A typed message protocol with request/response and publish/subscribe patterns.
- A C# server/host API for the sidecar.
- A TypeScript client API for the Electron main process (or renderer via IPC).
- Transport abstraction over stdio or a local socket.

### VoxelForge Usage Pattern

```text
Electron renderer  --(IPC)-->  Electron main  --(den-bridge client)-->  C# sidecar
     TS UI/Three.js                Node.js                        VoxelForge.App services
```

- The Electron **main** process owns the `den-bridge` client connection and spawns the C# sidecar as a child process over stdio (or local socket if stdio proves insufficient).
- The Electron **renderer** process communicates with main over Electron IPC.
- The C# sidecar hosts the `den-bridge` server and exposes VoxelForge-specific message handlers.

### Upstream-First Policy

`den-bridge` is shared upstream infrastructure consumed as a git submodule at `lib/den-bridge`. Improvements to the bridge itself—protocol features, transport hardening, C# or TypeScript API changes—should generally happen in the upstream `den-bridge` repository rather than in VoxelForge. Any VoxelForge-specific integration must live in thin adapter code outside the submodule (e.g., `VoxelForge.Bridge` message handlers or Electron main-process wrappers). Temporary VoxelForge-only bridge workarounds must be tracked with a follow-up task or note so they can be upstreamed and the local workaround removed.

### Why den-bridge Only

Using a single communication path:

1. Preserves the architecture boundary: the renderer cannot accidentally reach around the bridge to mutate state.
2. Makes the protocol explicit and testable in isolation.
3. Keeps `den-bridge` itself as the focus of upstream improvement rather than accumulating one-off transports inside VoxelForge.
4. Aligns with the existing adapter rule: adapters parse transport input, call typed services, and format responses ([`events-states-services.md`](events-states-services.md)).

---

## Repo Layout Proposal

This layout is additive. No existing source projects are removed or renamed during the experiment.

```text
voxelforge/
├── src/
│   ├── VoxelForge.Core                    (unchanged)
│   ├── VoxelForge.Content                 (unchanged)
│   ├── VoxelForge.LLM                     (unchanged)
│   ├── VoxelForge.App                     (unchanged)
│   ├── VoxelForge.Engine.MonoGame         (unchanged — FNA/Myra control path)
│   ├── VoxelForge.Mcp                     (unchanged — headless MCP adapter)
│   ├── VoxelForge.Evaluation              (unchanged)
│   ├── VoxelForge.Import                  (unchanged)
│   ├── VoxelForge.Bridge                  (NEW) — C# sidecar entry point and bridge adapters
│   │   ├── Program.cs                     — sidecar composition root, den-bridge host setup
│   │   ├── Adapters/
│   │   │   ├── VoxelForgeMessageHandler.cs   — bridge message dispatch to App services
│   │   │   ├── MeshSnapshotAdapter.cs        — mesh snapshot serialization for TS renderer
│   │   │   └── EditorStateAdapter.cs         — state delta serialization for TS UI
│   │   └── Protocol/
│   │       ├── Messages.cs                — C# DTOs for bridge message types
│   │       └── Schema/                    — JSON Schema or TypeScript typings generation target
│   └── ...
│
├── electron/                              (NEW) — Electron application
│   ├── package.json
│   ├── tsconfig.json
│   ├── src/
│   │   ├── main/
│   │   │   ├── index.ts                   — main process entry, window creation, sidecar spawn
│   │   │   └── bridge-client.ts           — den-bridge client wrapper over stdio/socket
│   │   ├── preload/
│   │   │   └── index.ts                   — preload script exposing safe IPC to renderer
│   │   └── renderer/
│   │       ├── index.ts                   — renderer entry
│   │       ├── renderer/
│   │       │   ├── three-scene.ts         — Three.js scene, camera, lights
│   │       │   ├── voxel-mesh-view.ts     — mesh snapshot -> Three.js mesh
│   │       │   └── camera-controller.ts   — orbit, zoom, snap controls
│   │       ├── ui/
│   │       │   ├── panels/
│   │       │   └── components/
│   │       └── bridge-ipc.ts              — renderer-side bridge message routing over IPC
│   └── build/
│       ├── electron-builder.yml           — packaging config (defer to task #1180)
│       └── vite.config.ts                 — renderer bundler config
│
├── docs/
│   ├── architecture/
│   │   ├── electron-renderer-experiment.md   (this file)
│   │   ├── bridge-protocol.md             — message schema, versioning, lifecycle (defined in task #1172)
│   └── ...
│
├── scripts/
│   ├── build-electron.sh                  — build C# sidecar + npm install + bundle renderer
│   ├── run-electron-dev.sh                — dev loop: build sidecar + start Electron with hot reload
│   └── run-electron-prod.sh               — production launch
│
├── lib/
│   ├── den-bridge/                        (NEW in task #1170) — git submodule
│   ├── FNA/                               (unchanged)
│   ├── Myra/                              (unchanged)
│   └── ...
│
├── tests/
│   ├── VoxelForge.Core.Tests              (unchanged)
│   ├── VoxelForge.App.Tests               (unchanged)
│   ├── VoxelForge.Bridge.Tests            (NEW) — protocol round-trip, adapter behavior, state delta tests
│   ├── Architecture.Tests                 (unchanged — add Electron boundary rules)
│   └── electron/                          (NEW in later tasks) — TS-side bridge client and renderer tests
│
└── voxelforge.slnx                        (add VoxelForge.Bridge project reference)
```

### Layout Rules

- `VoxelForge.Bridge` references `VoxelForge.App` and `VoxelForge.Core` only. It does not reference `Engine.MonoGame`, FNA, or Myra.
- `electron/` is a separate npm/TypeScript project. It does not import C# sources or vice versa.
- `den-bridge` lives in `lib/den-bridge` as a submodule, not copied into `src/` or `electron/`.
- Existing `VoxelForge.Engine.MonoGame` and `VoxelForge.Mcp` remain unchanged and independent.

---

## C# Sidecar Design Sketch

The sidecar is a console application with a `den-bridge` host loop. It is headless: no FNA window, no Myra UI.

### Composition Root (`VoxelForge.Bridge/Program.cs`)

1. Compose the same `VoxelForge.App` service graph used by `Engine.MonoGame` and `Mcp`.
2. Register `den-bridge` message handlers that wrap App services.
3. Start the bridge host (stdio or socket).
4. On bridge disconnect or sidecar stdin EOF, shut down cleanly.

### Bridge Message Handler Categories

| Category | Example Messages | Direction |
|----------|------------------|-----------|
| **State subscription** | `subscribe_editor_state`, `state_delta` | C# → TS (push) |
| **Mesh snapshots** | `request_mesh`, `mesh_snapshot` | TS → C# (request), C# → TS (response/push) |
| **Interaction intents** | `place_voxel_intent`, `remove_voxel_intent`, `paint_voxel_intent`, `camera_orbit_intent` | TS → C# (request) |
| **Command execution** | `execute_command`, `command_result` | TS → C# (request) |
| **Tool activation** | `set_active_tool`, `tool_options` | TS → C# (request) |
| **Undo/redo** | `undo`, `redo` | TS → C# (request) |
| **Persistence** | `save_project`, `load_project`, `project_list` | TS → C# (request) |
| **Session control** | `new_model`, `publish_preview` | TS → C# (request) |

### Mesh Snapshot Service

Task #1174 introduces renderer-neutral mesh services. The sidecar uses those services to produce compact mesh snapshots:

- Vertex buffer + index buffer arrays (or glTF-compatible buffer views).
- Palette index per face/group for TS material lookup.
- Optional incremental update format for task #1176.

The snapshot format is defined in the bridge protocol (task #1172) and versioned independently of `.vforge` serialization.

---

## Bridge Protocol Lifecycle

The full VoxelForge bridge protocol is defined in [`bridge-protocol.md`](bridge-protocol.md). At a high level:

1. **Spawn:** Electron main spawns `VoxelForge.Bridge` as a child process, passing stdio or socket path.
2. **Handshake:** `den-bridge` performs transport-level version handshake, followed by the VoxelForge schema handshake (`voxelforge.handshake`). If either version is incompatible, Electron main shows an error and refuses to load the renderer.
3. **Subscribe:** Electron renderer requests state subscription. C# sidecar begins pushing state deltas and mesh snapshots.
4. **Interaction Loop:** TS captures input, sends intents, C# applies via services, pushes updated state/mesh.
5. **Teardown:** On Electron window close, main sends graceful shutdown. Sidecar exits cleanly. Unsaved in-memory state follows the same rules as `VoxelForge.Mcp`: it is lost unless explicitly saved.

> **Note:** The current smoke-test implementation from task #1171 (`ping` and `version.handshake`) is a den-bridge connectivity probe, not the full VoxelForge protocol. See `bridge-protocol.md` for the complete message contract.

---

## FNA/Myra Control Path Preservation

During the entire experiment:

- `VoxelForge.Engine.MonoGame` remains the supported interactive editor.
- FNA/Myra continues to receive bug fixes and feature parity work required by the existing roadmap.
- The Electron renderer does not claim feature completeness until the final decision checkpoint (#1181).
- Live preview and MCP workflows ([`docs/mcp-server.md`](../mcp-server.md)) continue to function independently of the Electron path. The Electron renderer is an additional viewer/editor surface, not a replacement for MCP headless sessions or preview watchers.
- If an implementation choice would require changing FNA/Myra internals, prefer adding a neutral service in `VoxelForge.App` rather than coupling the two renderers.

---

## Success Metrics

### First Vertical Slice (task #1177)

The vertical slice is considered successful when:

1. **Sidecar starts:** `VoxelForge.Bridge` launches from Electron main, handshakes over `den-bridge`, and composes the App service graph without FNA/Myra references.
2. **Mesh display:** A `.vforge` file opened via the sidecar renders as a colored voxel mesh in the Three.js canvas with correct palette colors.
3. **Camera control:** Orbit, zoom, and snap-to-view work in the renderer without C# round-trips.
4. **Single mutation round-trip:** Placing or removing one voxel via TS input flows through bridge → C# service → mesh regeneration → TS mesh update, end-to-end in under 100 ms on reference hardware.
5. **Undo works:** Ctrl+Z in the Electron window sends `undo` through the bridge and the mesh reverts.
6. **No forbidden TS behavior:** Architecture tests or code review confirm no direct voxel mutation, undo logic, persistence, or MCP routing in TS.

### Final Decision Checkpoint (task #1181)

Before deciding whether to adopt, extend, or abandon the Electron renderer:

| Criterion | Threshold |
|-----------|-----------|
| **Feature parity** | Electron renderer supports all tools, labels, animation, and reference image workflows that FNA/Myra supports today, or has a documented gap list with migration plan. |
| **Performance** | Frame time on reference scenes is within 2x of FNA renderer at equivalent quality. Mesh update latency stays under 100 ms for edits up to 1,000 voxels. |
| **Reliability** | Bridge disconnection, sidecar crash, or renderer crash is recoverable without data loss (unsaved state warning + graceful reconnect). |
| **Build complexity** | `scripts/build-electron.sh` and `scripts/run-electron-dev.sh` run on Linux and macOS without manual steps beyond `git submodule update --init --recursive`. |
| **Maintainer cost** | Bridge protocol, TS renderer, and sidecar adapters have test coverage ≥ the existing FNA renderer test coverage (or a documented plan to reach it). |
| **MCP/live preview preservation** | Existing `VoxelForge.Mcp` and `--watch` preview workflows remain fully functional and documented. |

### Decision Outcomes

- **Adopt:** Make Electron the primary interactive renderer. FNA/Myra moves to legacy maintenance or is removed in a future task.
- **Extend:** Keep both renderers supported. Electron is the default on some platforms; FNA/Myra remains the default on others.
- **Abandon:** Remove `electron/`, `VoxelForge.Bridge`, and `lib/den-bridge`. Document lessons learned. FNA/Myra remains the sole renderer.

---

## Cross-References

- [`bridge-protocol.md`](bridge-protocol.md) — complete message schema, versioning rules, ownership annotations, and mesh payload strategy.
- [`events-states-services.md`](events-states-services.md) — adapter/service rules that apply to the bridge handlers.
- [`state-boundaries.md`](state-boundaries.md) — state ownership map that C# retains and TS observes.
- [`docs/mcp-server.md`](../mcp-server.md) — headless MCP adapter that remains independent of the Electron path.
- [`mcp-live-preview-follow-up-options.md`](mcp-live-preview-follow-up-options.md) — prior evaluation of push/launcher patterns; the Electron path replaces the "viewer" concept with a full renderer/editor surface.
- [`../mcp-server.md`](../mcp-server.md) — existing headless session and `publish_preview` workflow.

---

## Task Sequence Reference

| Task | Title | Dependency |
|------|-------|------------|
| #1169 | Write Electron renderer architecture plan | — |
| #1170 | Add den-bridge as shared submodule | #1169 |
| #1171 | Smoke-test C# sidecar plus Electron bridge | #1170 |
| #1172 | Define VoxelForge bridge protocol | #1171 |
| #1173 | Upstream reusable den-bridge hardening | #1172 |
| #1174 | Introduce renderer-neutral C# snapshot and mesh services | #1172 |
| #1175 | Create Electron and Three.js static mesh renderer | #1174 |
| #1176 | Add incremental mesh update pipeline | #1175 |
| #1177 | Build first Electron UI vertical slice | #1175, #1176 |
| #1178 | Wire Electron editing interactions through C# commands | #1177 |
| #1179 | Preserve live preview and MCP workflows in Electron path | #1178 |
| #1180 | Add Electron packaging and dev workflow | #1177 |
| #1181 | Electron renderer experiment decision checkpoint | #1179, #1180 |
