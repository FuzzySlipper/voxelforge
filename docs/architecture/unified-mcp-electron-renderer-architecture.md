# Unified MCP/Electron Renderer Architecture

> Task: #1656 | Parent: #1655
> Status: ADR / implementation plan
> Prepared: 2026-05-24T21:49:21-07:00
> Den source: `voxelforge/unified-mcp-electron-renderer-architecture-1656`

## Decision summary

VoxelForge will converge the MCP browser viewer and Electron renderer around **shared C# App-layer state/service semantics** and a **shared TypeScript renderer-core**, rather than continuing the current split between MCP inline JavaScript and a separate Electron/Bridge renderer path.

The chosen path is the larger service-extraction refactor from epic #1655:

1. Extract a shared workspace/session **state model** and stateless App services for commands, render-scene snapshots, reference-model operations, and render events.
2. Expose the same versioned render/command/event concepts through MCP HTTP/SSE and Electron/den-bridge transports.
3. Replace duplicated browser renderer logic with a shared TypeScript renderer-core consumed by both the MCP viewer shell and the Electron shell.
4. Delete or shrink old renderer paths after validation instead of leaving compatibility scaffolding as an attractive alternate path.

This deliberately does **not** choose "make MCP spawn or reuse the current separate `VoxelForge.Bridge` sidecar" as the primary architecture. The current Bridge sidecar has its own `VoxelModelHolder` and lacks MCP reference-model/material parity; using it directly would preserve two divergent authoritative state holders instead of fixing the architecture.

## Why this is necessary

The reference-model visibility and UV/material issues were not just isolated viewer bugs. They exposed a structural problem:

- MCP `/viewer` renders from a large inline JavaScript file at `src/VoxelForge.Mcp/wwwroot/viewer.html`.
- Electron renders from TypeScript under `electron/src/renderer`, using `VoxelForge.Bridge` and den-bridge.
- MCP currently has richer reference-model rendering payloads and texture serving.
- Electron has better module/test structure, but less reference-model/material parity.
- MCP and Bridge both use some shared App services, but they host different state holders:
  - MCP: `VoxelForgeMcpSession`
  - Bridge: `VoxelModelHolder`
- Both transports build or map render data differently around the same `MeshSnapshotService`.

The fix is not another one-off field added to `viewer.html`. The green path is one render-scene/material contract and one renderer-core implementation.

## Architectural rules

This plan inherits the required Den convention `voxelforge/renderer-frontend-backend-boundary-code-style`.

Hard rules:

- No new inline executable JavaScript.
- No JavaScript/TypeScript embedded in C# string literals.
- No browser/rendering logic accumulating in C# endpoint files.
- C# owns durable editor/reference/model state, command validation, undo/redo, persistence, import/voxelization, mesh/snapshot production, MCP tools, backend diagnostics, and texture authorization.
- TypeScript owns presentation, Three.js/WebGL objects, camera/pointer/UI state, renderer debug modes, capture readiness, and shell-specific UI.
- Cross-boundary payloads must be explicit, named, versioned, and auditable.
- App services must respect the Events/States/Services architecture: mutable truth lives in explicit `*State` types; services operate over explicit state arguments and do not hide durable mutable state in service instance fields.

## Target architecture

```text
VoxelForge.App / Core / Content
  ├─ VoxelForgeWorkspaceState                    # shared App-layer mutable truth shape
  ├─ WorkspaceCommandApplicationService          # stateless command/application operations
  ├─ ReferenceModelApplicationService            # stateless reference operations over state
  ├─ RenderSceneSnapshotService                  # stateless render-scene/material snapshot builder
  ├─ RenderSceneEventProjector                   # typed App events -> render events/revisions
  └─ MeshSnapshotService / PaletteSnapshotService / EditorSnapshotService

Transport hosts
  ├─ VoxelForge.Mcp
  │    ├─ MCP tools as adapters over App services
  │    ├─ HTTP/SSE render protocol adapter
  │    ├─ safe texture/asset URL adapter
  │    ├─ Chromium capture host adapter
  │    └─ tiny static viewer shell loading bundled TS
  └─ VoxelForge.Bridge + Electron
       ├─ den-bridge protocol adapter
       ├─ Electron main/preload shell
       └─ richer desktop UI shell

TypeScript
  ├─ renderer-core
  │    ├─ protocol types/normalizers
  │    ├─ scene/camera/mesh/material/reference rendering
  │    ├─ debug visual modes
  │    ├─ capture readiness
  │    └─ protocol-client interface
  ├─ mcp-viewer shell adapter
  └─ electron shell adapter
```

MCP and Electron may still be separate processes or hosts, but they must use the same **state/service semantics**, **render-scene/material contract**, and **renderer-core interpretation**. Their split should be shell/UI context only:

- MCP shell: agent/browser validation, headless capture, minimal controls, served by MCP host.
- Electron shell: desktop chrome, menu/filesystem integration, panels/tools, richer editing UX.

## C# App-layer design

### 1. Shared workspace state

Create a shared App-layer state aggregate, likely under `src/VoxelForge.App`, for the state that is currently split between `VoxelForgeMcpSession` and `VoxelModelHolder`.

Suggested type:

```csharp
namespace VoxelForge.App.Workspaces;

public sealed class VoxelForgeWorkspaceState
{
    public EditorDocumentState Document { get; } = new();
    public EditorSessionState Session { get; } = new();
    public UndoHistoryState UndoHistory { get; } = new();
    public UndoStack UndoStack { get; }
    public ReferenceModelState ReferenceModels { get; } = new();
    public ReferenceImageState ReferenceImages { get; } = new();

    public string ModelId { get; set; } = "default";
    public string? ProjectPath { get; set; }
    public string? CurrentModelName { get; set; }
    public bool IsDirty { get; set; }
    public string StatusMessage { get; set; } = "Ready";
    public long Revision { get; set; }
}
```

Naming is flexible, but the ownership is not: this is a `*State` shape, not a service hiding state. MCP and Bridge can each host a singleton state instance, but the state shape and operations should be common.

Move or adapt from:

- `src/VoxelForge.Mcp/VoxelForgeMcpSession.cs`
- `src/VoxelForge.Bridge/Handlers/VoxelModelHolder.cs`

Keep host-only details out of shared state:

- MCP SSE subscriber channels
- HTTP request/response details
- Chromium capture process configuration
- Bridge WebSocket connection/subscription objects
- Electron IPC/window state

### 2. Shared command/application service

Create or extend stateless services that operate on `VoxelForgeWorkspaceState`.

Suggested service shape:

```csharp
public sealed class WorkspaceCommandApplicationService
{
    public ApplicationServiceResult<WorkspaceCommandResult> Execute(
        VoxelForgeWorkspaceState workspace,
        WorkspaceCommandRequest request);

    public ApplicationServiceResult<WorkspaceHistoryResult> Undo(
        VoxelForgeWorkspaceState workspace);

    public ApplicationServiceResult<WorkspaceHistoryResult> Redo(
        VoxelForgeWorkspaceState workspace);
}
```

Responsibilities:

- Interpret typed command intents from adapters.
- Apply model/document/reference changes through existing App services and undoable commands.
- Mark dirty/status/revision consistently.
- Return emitted `IApplicationEvent` facts.

Adapters should parse transport-specific input and call this service. They should not rebuild command strings or duplicate mutation sequencing.

### 3. Shared reference-model application service

Reference-model support must not remain MCP-only.

Create or extend a stateless service for operations currently spread across MCP reference tools and viewer endpoints:

- load/list/remove/clear reference models;
- transform reference model;
- fit/suggest transform;
- set manual texture override;
- inspect materials;
- diagnostics/raycast/histogram/silhouette probes;
- resolve session-authorized texture or source-asset handles.

Suggested location:

- `src/VoxelForge.App/Reference/ReferenceModelApplicationService.cs`
- or `src/VoxelForge.App/Services/ReferenceModelApplicationService.cs`

Transport hosts remain responsible for how a texture handle becomes a URL. The App service decides whether a texture/source asset is authorized for the current loaded session.

### 4. Shared render-scene snapshot service

Create a `RenderSceneSnapshotService` in App. It should build renderer-neutral snapshots from the workspace state and existing mesh/palette/reference services.

Suggested shape:

```csharp
public sealed class RenderSceneSnapshotService
{
    public RenderSceneSnapshot BuildSnapshot(
        VoxelForgeWorkspaceState workspace,
        RenderSceneSnapshotRequest request);

    public RenderSceneState BuildState(
        VoxelForgeWorkspaceState workspace);
}
```

Responsibilities:

- Call `MeshSnapshotService` for voxel mesh data.
- Call `PaletteSnapshotService` for palette state.
- Include reference-model render nodes/primitives/materials/textures.
- Compute voxel bounds, reference bounds, and combined bounds once.
- Include diagnostics needed by both MCP and Electron.
- Produce a versioned DTO independent of HTTP, SSE, den-bridge, or Three.js.

Move or absorb logic from:

- `src/VoxelForge.Mcp/Viewer/ViewerEndpoints.cs` reference model data builders;
- `src/VoxelForge.Bridge/Handlers/MeshSnapshotHandler.cs` snapshot mapping;
- `src/VoxelForge.Bridge/Handlers/EditorUiStateBridgeService.cs` duplicated state summary mapping.

### 5. Render-event projector

Bridge currently has richer event/subscription plumbing while MCP mainly has revision notifications. Unify the semantic event model without requiring identical transport mechanics.

Suggested service/policy:

```csharp
public sealed class RenderSceneEventProjector
{
    public IReadOnlyList<RenderSceneEvent> Project(IApplicationEvent applicationEvent, VoxelForgeWorkspaceState workspace);
}
```

Responsibilities:

- Convert App events into render-scene event kinds.
- Maintain a monotonic sequence/revision in workspace state.
- Distinguish state changes, mesh changes, palette changes, reference changes, and diagnostics.

Bridge can publish these over den-bridge. MCP can initially publish `snapshot_required` or revision events over SSE, but the event names should be the same semantic contract.

## Versioned render-scene/material contract

Replace ad hoc MCP `reference_models` payloads and Bridge-only mesh DTOs with one canonical render scene contract.

Use snake_case JSON for browser/HTTP payloads unless the existing den-bridge convention explicitly requires otherwise. Transport adapters may wrap or encode arrays differently, but the semantic DTO should be common.

Minimum contract:

```text
RenderSceneSnapshot
  schema_version: "voxelforge.render_scene@1"
  revision: number
  model_id: string
  source: { host: "mcp" | "bridge" | "test"; capabilities: string[] }
  bounds: Bounds | null                         # voxel-only bounds
  reference_bounds: Bounds | null
  combined_bounds: Bounds | null
  voxel_meshes: RenderVoxelMesh[]
  reference_nodes: RenderReferenceNode[]
  materials: RenderMaterial[]
  textures: RenderTexture[]
  palette: PaletteEntry[]
  diagnostics: RenderDiagnostic[]
```

### Voxel mesh

```text
RenderVoxelMesh
  id: string
  revision: number
  positions: number[] | binary-handle
  normals: number[] | binary-handle
  colors_rgba: number[] | binary-handle
  palette_indices: number[] | binary-handle
  indices: number[] | binary-handle
  bounds: Bounds | null
  payload_format: "json_arrays" | "base64" | "binary"
```

Voxel meshes remain backend-generated because greedy meshing is C# App/Core behavior.

### Reference nodes and primitives

```text
RenderReferenceNode
  id: string
  display_name: string
  source_format: string
  source_asset_id?: string
  visible: boolean
  render_mode: "textured" | "wireframe" | "points" | "hidden"
  transform: Transform
  bounds_local: Bounds | null
  bounds_world: Bounds | null
  primitives: RenderPrimitive[]
  diagnostics: RenderDiagnostic[]
```

```text
RenderPrimitive
  id: string
  mesh_index: number
  material_id: string
  attributes:
    position: number[] | binary-handle
    normal?: number[] | binary-handle
    color_rgba?: number[] | binary-handle
    uv_sets?: RenderUvSet[]
  indices?: number[] | binary-handle
  bounds_local: Bounds | null
```

### Materials and textures

The material contract must encode the fields that caused current fidelity issues:

```text
RenderMaterial
  id: string
  name: string
  base_color_factor: [number, number, number, number]
  base_color_texture?: TextureSlot
  normal_texture?: TextureSlot
  emissive_texture?: TextureSlot
  emissive_factor?: [number, number, number]
  metallic_factor?: number
  roughness_factor?: number
  alpha_mode: "opaque" | "mask" | "blend"
  alpha_cutoff?: number
  double_sided: boolean
  color_space: "srgb" | "linear" | "unknown"
  diagnostics: RenderDiagnostic[]
```

```text
TextureSlot
  texture_id: string
  uv_set: number
  uv_transform:
    offset: [number, number]
    scale: [number, number]
    rotation: number
  uv_origin: "top_left" | "bottom_left" | "asset_defined" | "unknown"
  flip_y: boolean | "asset_defined"
  wrap_s: "clamp" | "repeat" | "mirror" | "unknown"
  wrap_t: "clamp" | "repeat" | "mirror" | "unknown"
  source_label: "assimp" | "unity_sidecar" | "manual_override" | "generated" | "unknown"
```

```text
RenderTexture
  id: string
  uri: string                                  # session-authorized URL or transport handle
  mime_type?: string
  color_space: "srgb" | "linear" | "unknown"
  width?: number
  height?: number
  diagnostics: RenderDiagnostic[]
```

The key point is that UV origin/flip convention, UV set, texture transform, color space, alpha mode, sidedness, wrapping, and source label are first-class data. They must not be rediscovered separately in MCP inline JS and Electron TS.

## Command intents vs snapshots vs events

Keep the protocol concepts separate.

### Command intents

Command intents are requests from a shell/user/agent to mutate authoritative C# state. Examples:

- place/remove/fill voxels;
- load/save project;
- load/transform/remove reference model;
- set reference texture override;
- undo/redo;
- set palette entry.

They flow:

```text
MCP tool / Electron UI / future shell
  -> transport adapter DTO
  -> WorkspaceCommandApplicationService or ReferenceModelApplicationService
  -> explicit workspace state + App services + events
  -> command result + render events
```

TS may capture input and send intents, but it must not apply durable model mutations itself.

### Render snapshots

Render snapshots are read-only C# projections for presentation. They are not authoritative state and must not be mutated by TS.

They flow:

```text
VoxelForgeWorkspaceState
  -> RenderSceneSnapshotService
  -> transport serializer
  -> renderer-core snapshot normalizer
  -> Three.js/WebGL scene update
```

### Events

Events tell the viewer what changed and when to request/apply updates.

Semantic event kinds:

```text
render.state_changed
render.mesh_changed
render.palette_changed
render.reference_changed
render.snapshot_required
render.diagnostics
```

Bridge may carry richer incremental payloads. MCP may initially use full-snapshot revision events. The event names and meaning should still be shared.

## Transport/discovery strategy

### MCP browser viewer

MCP should expose a versioned render protocol over HTTP/SSE first, because that fits the current viewer/capture shell and avoids coupling MCP to the existing separate Bridge sidecar.

Suggested endpoints:

```text
GET /api/render/handshake
GET /api/render/state
POST /api/render/snapshot
GET /api/render/events
GET /api/render/texture/{texture_id}
GET /api/render/asset/{asset_id}        # optional, safe session-scoped source-asset serving
```

Existing endpoints may be transitional aliases:

- `/api/viewer-state`
- `/api/mesh-snapshot`
- `/api/palette`
- `/api/viewer-events`
- `/api/reference-texture`

Each alias should have a removal note once the new renderer shell ships.

### Electron / Bridge

Bridge can continue using den-bridge, but its messages should wrap the same render concepts.

Suggested Bridge message names:

```text
voxelforge.render.handshake
voxelforge.render.request_state
voxelforge.render.request_snapshot
voxelforge.render.subscribe
voxelforge.command.execute
voxelforge.history.undo
voxelforge.history.redo
voxelforge.project.load
voxelforge.project.save
```

Existing messages may remain as migration aliases only if documented and deleted/shrunk in #1659.

### TypeScript protocol client interface

Renderer-core should consume a transport-neutral client interface:

```ts
export interface RenderProtocolClient {
  handshake(): Promise<RenderHandshake>;
  requestState(): Promise<RenderSceneState>;
  requestSnapshot(request?: RenderSceneSnapshotRequest): Promise<RenderSceneSnapshot>;
  subscribe(listener: (event: RenderSceneEvent) => void): Promise<Subscription>;
  executeCommand?(request: WorkspaceCommandRequest): Promise<WorkspaceCommandResponse>;
}
```

Implementations:

- `HttpSseRenderClient` for MCP viewer.
- `DenBridgeRenderClient` for Electron.
- fixture/mock client for tests.

## TypeScript renderer-core design

Initial local location can be under `electron/src/renderer-core` to minimize build-system churn. If the module grows or needs separate packaging, move it to `packages/renderer-core` in a later cleanup.

Suggested modules:

```text
electron/src/renderer-core/
  protocol/
    types.ts
    normalizeSnapshot.ts
    byteArrays.ts
  scene/
    VoxelForgeScene.ts
    voxelMesh.ts
    referenceModels.ts
    materials.ts
    camera.ts
    picking.ts
  transport/
    RenderProtocolClient.ts
    DenBridgeRenderClient.ts
    HttpSseRenderClient.ts
  diagnostics/
    captureReady.ts
    debugModes.ts
    fps.ts
```

Move into renderer-core from Electron:

- `electron/src/renderer/scene.ts` scene orchestration, mesh buffer construction, camera framing, picking, view snaps;
- `electron/src/shared/byte-utils.ts`;
- `electron/src/shared/compute-placement.ts`;
- `electron/src/shared/refresh-coalescer.ts` where applicable.

Move into renderer-core from MCP inline viewer:

- reference model group/primitive rendering;
- texture loading/material setup;
- UV debug modes;
- combined bounds/camera framing;
- capture readiness accounting.

Keep outside renderer-core:

- Electron BrowserWindow/main/preload IPC;
- Electron menus/file dialogs/native lifecycle;
- MCP HTML/CSS shell and query-param parsing;
- MCP HTTP endpoint code;
- C# capture service process launch.

## Migration plan and task handoff

### #1657: shared C# session/render services

Recommended branch:

```bash
git switch -c task/1657-shared-render-services
```

Implementation slices:

1. Add `VoxelForgeWorkspaceState` in App.
   - Include document/session/undo/reference/project/status/revision state currently split across MCP and Bridge.
   - Do not make it a service with hidden mutable state.
2. Add App DI registration helpers.
   - Example: `AddVoxelForgeWorkspaceCore`, `AddVoxelForgeRenderSceneServices`.
   - Keep transport registration in MCP/Bridge.
3. Add `RenderSceneSnapshot` DTOs and `RenderSceneSnapshotService`.
   - Start with existing voxel mesh + palette + bounds parity.
   - Then add reference nodes/materials/textures/diagnostics from MCP path.
4. Add reference-model application/authorization service.
   - Move texture authorization and material/texture descriptor construction out of endpoint code.
5. Adapt MCP to use the shared workspace state and render service.
   - `VoxelForgeMcpSession` should shrink to MCP-specific revision/SSE/capture adapter state or disappear.
6. Adapt Bridge handlers to use shared workspace state and render service.
   - `VoxelModelHolder` should shrink/disappear.
7. Add C# tests and architecture gates.
   - Snapshot DTO serialization tests.
   - Material/texture fields present.
   - MCP and Bridge adapters produce equivalent render-scene snapshots for the same workspace fixture.
   - Architecture tests prevent MCP/Bridge from referencing each other and keep services state-clean.

Primary files likely touched:

- `src/VoxelForge.App/**`
- `src/VoxelForge.Mcp/VoxelForgeMcpSession.cs`
- `src/VoxelForge.Mcp/Viewer/ViewerEndpoints.cs`
- `src/VoxelForge.Mcp/Program.cs`
- `src/VoxelForge.Bridge/Handlers/VoxelModelHolder.cs`
- `src/VoxelForge.Bridge/Handlers/MeshSnapshotHandler.cs`
- `src/VoxelForge.Bridge/Handlers/EditorUiStateBridgeService.cs`
- `src/VoxelForge.Bridge/Protocol/VoxelForgeMessages.cs`
- `tests/VoxelForge.App.Tests/**`
- `tests/VoxelForge.Mcp.Tests/**`
- `tests/VoxelForge.Bridge.Tests/**`
- `tests/Architecture.Tests/**`

### #1658: shared TypeScript renderer-core and MCP inline viewer replacement

Recommended branch:

```bash
git switch -c task/1658-shared-renderer-core
```

Implementation slices:

1. Create renderer-core modules and tests.
   - Extract pure snapshot normalization and byte-array handling first.
   - Add fixture tests before moving scene behavior.
2. Extract Electron scene pieces into renderer-core.
   - Keep Electron shell behavior unchanged at first.
3. Add reference-model/material renderer-core modules.
   - Port MCP reference rendering from `viewer.html` into typed TS.
   - Implement material contract fields from #1657.
4. Add `HttpSseRenderClient` for MCP.
   - Consume `/api/render/*` endpoints.
5. Replace MCP inline viewer logic.
   - `src/VoxelForge.Mcp/wwwroot/viewer.html` becomes a static shell loading a bundled module.
   - No substantial inline executable JS remains.
6. Update build scripts.
   - Extend `electron/package.json` or root scripts so MCP viewer bundle is built deterministically.
   - Avoid adding a large framework unless necessary; current `tsc + esbuild` is sufficient.
7. Add browser/capture smoke.
   - Verify voxel mesh and reference model render through shared path.
   - Verify capture readiness waits for assets/textures.

Primary files likely touched:

- `electron/src/renderer-core/**` or future `packages/renderer-core/**`
- `electron/src/renderer/index.ts`
- `electron/src/renderer/scene.ts`
- `electron/src/shared/**`
- `electron/tests/**`
- `electron/package.json`
- `src/VoxelForge.Mcp/wwwroot/viewer.html`
- new MCP viewer TS entrypoint/source directory selected by implementer
- `src/VoxelForge.Mcp/VoxelForge.Mcp.csproj` if static asset embedding/build copy changes
- `scripts/**` for build/smoke helpers if needed

### #1659: cleanup, parity validation, and RuleWeaver transfer note

Recommended branch:

```bash
git switch -c task/1659-renderer-cleanup-parity-docs
```

Implementation slices:

1. Delete or quarantine obsolete endpoint aliases and old inline renderer code.
2. Delete/shrink `VoxelForgeMcpSession` and `VoxelModelHolder` leftovers if #1657 left transitional wrappers.
3. Remove duplicated MCP/Electron material interpretation paths.
4. Update repo docs and Den docs to name the green path.
5. Run full parity validation.
6. Write RuleWeaver transfer note.

Primary cleanup targets:

- substantial inline script in `src/VoxelForge.Mcp/wwwroot/viewer.html`;
- MCP `ViewerEndpoints.cs` DTO/render-builder logic that moved to App;
- Bridge `VoxelModelHolder` and duplicated snapshot services if replaced;
- stale protocol docs that imply Electron is a separate experiment rather than a shell over shared renderer concepts;
- obsolete `/api/viewer-*` aliases once `/api/render/*` is stable.

## Deterministic validation gates

Baseline for implementation branches:

```bash
git submodule update --init --recursive
dotnet build voxelforge.slnx
dotnet test voxelforge.slnx
cd electron && npm run build
cd electron && npm test
```

Additional gates for #1657:

```bash
dotnet test tests/VoxelForge.App.Tests/VoxelForge.App.Tests.csproj
dotnet test tests/VoxelForge.Mcp.Tests/VoxelForge.Mcp.Tests.csproj
dotnet test tests/VoxelForge.Bridge.Tests/VoxelForge.Bridge.Tests.csproj
dotnet test tests/Architecture.Tests/Architecture.Tests.csproj
```

Additional gates for #1658:

```bash
cd electron && npm run build
cd electron && npm test
./scripts/run-renderer-smoke-test.sh
```

MCP browser/capture smoke:

```bash
./scripts/ensure-mcp-web.sh
curl -fsS http://127.0.0.1:5201/health
curl -fsS http://127.0.0.1:5201/viewer >/tmp/voxelforge-viewer.html
curl -fsS http://127.0.0.1:5201/api/render/handshake
curl -fsS http://127.0.0.1:5201/api/render/state
```

If `/api/render/*` is not live yet during migration, use the existing `/api/viewer-state` and `/api/mesh-snapshot` as transitional smoke only, and record when the alias will be removed.

Electron/Bridge smoke when Bridge/Electron shell or sidecar discovery changes:

```bash
./scripts/run-bridge-smoke-test.sh
./scripts/run-electron-smoke-test.sh
./scripts/run-renderer-smoke-test.sh
cd electron && npm run package:dir   # only when packaging/sidecar discovery changes
```

Reference-model validation after #1658:

- Load Watcher, Apple GLB, and GOLEM/converted GLB if available from operator-local stash paths.
- Verify `RenderSceneSnapshot.reference_nodes[]` is non-empty and has bounds.
- Verify materials carry texture/UV metadata and diagnostics.
- Capture front/right/top/isometric via MCP headless viewer.
- Compare MCP and Electron display of the same snapshot fixture or saved scene.

## Architecture test additions

Extend `tests/Architecture.Tests` to guard this refactor:

- MCP and Bridge must not reference each other.
- Shared render/session DTOs/services must live in App or another explicitly chosen shared project, not in MCP or Bridge.
- App render services must not depend on UI/adapter/web/den-bridge types.
- No new inline executable JS. Allow a temporary whitelist for legacy `src/VoxelForge.Mcp/wwwroot/viewer.html` only until #1658/#1659 removes it.
- The unified renderer ADR must exist and mention:
  - `VoxelForgeMcpSession`
  - `VoxelModelHolder`
  - `RenderSceneSnapshot`
  - `renderer-core`
  - no inline JavaScript
  - MCP browser smoke
  - Electron parity smoke
  - RuleWeaver transfer pattern

## Old path removal plan

Transitional only:

- `/api/viewer-state`
- `/api/mesh-snapshot`
- `/api/palette`
- `/api/viewer-events`
- `/api/reference-texture`
- Bridge `voxelforge.mesh.*` messages that do not expose full render-scene/material semantics
- `VoxelForgeMcpSession` as durable state holder
- `VoxelModelHolder` as durable state holder
- large inline script in `viewer.html`

Removal criteria:

- `/api/render/*` or equivalent versioned render protocol is live.
- MCP viewer shell consumes renderer-core bundle.
- Electron shell consumes renderer-core for the same snapshot semantics.
- Tests prove equivalent render-scene snapshots for MCP/Bridge fixtures.
- MCP capture smoke and Electron smoke pass.
- Den task #1659 records deleted/quarantined files and remaining follow-ups.

## RuleWeaver transfer pattern

The reusable pattern for RuleWeaver’s future FNA-to-web-renderer migration is:

1. Keep C# authoritative for durable state, commands, undo/redo, persistence, imports, and diagnostics.
2. Extract explicit state objects and stateless application services before building transport/UI shells.
3. Define a versioned render-scene/material contract as the only presentation data boundary.
4. Build a shared TS renderer-core around that contract.
5. Make desktop/browser differences shell-only.
6. Add architecture tests that prevent inline JS, UI leakage into App/Core, and duplicate state holders.
7. Delete old renderer paths after smoke parity, rather than keeping compatibility scaffolding that future agents may mistake for the green path.

## Open questions for implementers

These should be answered during #1657/#1658, not by silently choosing ad hoc behavior:

- Should `RenderSceneSnapshot` arrays use JSON arrays initially everywhere, or should Bridge keep binary/base64 transport wrappers with a shared semantic normalizer?
- Should direct browser asset loading be included in #1658 or left as a later diagnostic feature after the normalized contract is stable?
- What exact path should hold renderer-core long-term: `electron/src/renderer-core` or `packages/renderer-core`?
- How quickly can old `/api/viewer-*` aliases be removed without disrupting current MCP tool capture workflows?

Default answers unless evidence says otherwise:

- Start with JSON arrays for simplicity and tests; isolate payload encoding so binary optimization is transport-level later.
- Keep direct asset loading as a diagnostic comparison mode, not the primary architecture for #1657/#1658.
- Start under `electron/src/renderer-core` to avoid build-system churn, then move to `packages/renderer-core` only if necessary.
- Remove old aliases in #1659 once capture and Electron smoke pass.
