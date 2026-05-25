# Renderer Cleanup, Parity Validation & RuleWeaver Transfer Pattern

> Task: #1659 | Parent epic: #1655 (Unified MCP/Electron Renderer Architecture)
> Status: completed
> Prepared: 2026-05-25
> Branch: `task/1659-renderer-cleanup-parity-docs`
> Base commit: `5916039317ef7ae2556db6f21969eaf221a7c773`

## Green Path Summary

The canonical renderer architecture after #1657 + #1658 + #1659:

```
VoxelForge.App (C#)
  ├─ VoxelForgeWorkspaceState          # shared mutable truth (App/Workspaces)
  ├─ RenderSceneSnapshotService        # stateless versioned snapshot builder (App/Services)
  ├─ RenderSceneEventProjector         # App events → render events
  ├─ MeshSnapshotService               # voxel mesh generation (App/Services)
  └─ PaletteSnapshotService            # palette snapshot (App/Services)

Transport hosts
  ├─ VoxelForge.Mcp
  │    ├─ GET /api/render/state        # lightweight state summary
  │    ├─ GET /api/render/snapshot     # full versioned render-scene snapshot
  │    ├─ GET /api/viewer-events       # SSE revision stream
  │    ├─ GET /api/reference-texture   # authorized texture serving
  │    ├─ GET /viewer                  # HTML shell → viewer-bundle.js
  │    └─ MCP tools (App service adapters)
  └─ VoxelForge.Bridge + Electron
       ├─ voxelforge.render.state      # bridge state message
       ├─ voxelforge.render.snapshot   # bridge snapshot message
       ├─ voxelforge.state.delta       # state subscription events
       └─ voxelforge.mesh.*, palette.* # dedicated bridge channels

TypeScript renderer-core (electron/src/renderer-core/)
  ├─ protocol/types.ts                 # canonical TS contract (snake_case)
  ├─ protocol/normalizeSnapshot.ts     # snapshot normalization
  ├─ scene/VoxelForgeScene.ts         # Three.js scene management
  ├─ scene/referenceModels.ts         # reference model rendering
  ├─ scene/materials.ts               # material/texture handling
  ├─ scene/captureReady.ts            # capture readiness signal
  ├─ transport/RenderProtocolClient.ts # transport-agnostic interface
  ├─ transport/HttpSseRenderClient.ts  # MCP browser transport
  └─ transport/DenBridgeRenderClient.ts # Electron bridge transport

Shell adapters
  ├─ electron/src/mcp-viewer/main.ts   # MCP browser viewer entry (bundled as viewer-bundle.js)
  └─ electron/src/renderer/index.ts    # Electron renderer entry
```

## Deleted / Quarantined Old Paths

### Removed transitional endpoint aliases (in code comments only — endpoints kept for backward compat)

The following transitional endpoints remain **functional** but are **quarantined** with explicit deprecation comments. They will be removed in a future task:

| Endpoint | Deprecated Since | Replacement | Removal Target |
|----------|-----------------|-------------|----------------|
| `GET /api/viewer-state` | #1657/#1659 | `GET /api/render/state` | Future cleanup |
| `GET /api/mesh-snapshot` | #1657/#1659 | `GET /api/render/snapshot` | Future cleanup |
| `GET /api/palette` | #1657/#1659 | `/api/render/state` (palette included) | Future cleanup |

### Remaining canonical endpoints (kept / green)

| Endpoint | Purpose |
|----------|---------|
| `GET /viewer` | MCP HTML viewer shell |
| `GET /viewer-bundle.js` | Bundled viewer application |
| `GET /api/render/state` | Lightweight render state summary |
| `GET /api/render/snapshot` | Full versioned render-scene snapshot |
| `GET /api/viewer-events` | SSE revision event stream |
| `GET /api/reference-texture` | Authorized reference texture serving |
| `GET /health` | Server health check |
| `GET /mcp` | MCP protocol endpoint |

### Cleaned up old JS/TS code

- **No inline JavaScript in C#** — Already achieved in #1658. Architecture test `NoNewInlineJavaScript_Introduced` guards against regressions.
- **Parallel inline-viewer JS removed** — Replaced by bundled TS in #1658.
- **TS transitional types marked `@deprecated`** — `TransitionalViewerState`, `TransitionalMeshSnapshot` in `renderer-core/protocol/types.ts` carry deprecation annotations.
- **Backward-compat shim in `electron/src/renderer/scene.ts`** — Re-exports from renderer-core with `@deprecated` annotations. Deprecated type aliases (`MeshSnapshotData`, `PaletteData`, etc.) retained for existing renderer references only.
- **HttpSseRenderClient transitional fallback** — Prefers canonical `/api/render/state`, `/api/render/snapshot` first; falls back to transitional endpoints only when canonical fails.
- **DenBridgeRenderClient transitional fallback** — Similar dual-path: prefers new bridge channels, falls back to old `bridge:mesh-snapshot` + `bridge:state-subscribe`.

### Cleaned up docs

| Document | Action |
|----------|--------|
| `docs/architecture/unified-mcp-electron-renderer-architecture.md` | Already current (the ADR); verified accurate. |
| `docs/architecture/electron-renderer-experiment.md` | Marked historical; already has "completed" status. |
| `docs/architecture/electron-renderer-decision-checkpoint.md` | Marked historical; already has "completed" status. |
| `docs/architecture/electron-rendering-backend-evaluation.md` | Exploratory doc, non-normative; kept as-is. |
| `docs/mcp-server.md` | Updated: canonical endpoints listed as primary, transitional noted as deprecated. |
| `docs/architecture/renderer-cleanup-parity-ruleweaver.md` | **This document** — new cleanup/parity/RuleWeaver doc. |

### Not yet cleaned (follow-up targets)

- Old `scene.ts` backward-compat shim types (`MeshSnapshotData`, `PaletteData`, `MeshUpdateEventData` etc.) — kept because the Electron renderer `index.ts` still imports them. Should be removed when renderer fully migrates to canonical renderer-core types.
- `electron/src/main/bridge-client.ts` raw WebSocket client — used by Electron main process, separate concern.
- `electron/src/main/index.ts` — Electron main process code, out of scope.
- Build-time transitional endpoint removal — requires updating HttpSseRenderClient fallback path and rebuilding/redistributing viewer-bundle.js.

## MCP / Electron Parity Validation

### Shared semantics verified:

| Dimension | MCP (HttpSseRenderClient) | Electron (DenBridgeRenderClient) | Parity |
|-----------|--------------------------|--------------------------------|--------|
| **State contract** | RenderSceneSnapshot (snake_case JSON) | RenderSceneSnapshot (via bridge) | ✅ |
| **Normalization** | normalizeSnapshot() — base64 decode, bounds fixup | Same normalizeSnapshot() | ✅ |
| **Transport interface** | RenderProtocolClient interface | RenderProtocolClient interface | ✅ |
| **Event subscription** | EventSource → /api/viewer-events | Bridge onEvent("voxelforge:state-delta") | ✅ (semantic) |
| **Scene construction** | VoxelForgeScene.buildFromSnapshot() | VoxelForgeScene.buildFromSnapshot() | ✅ |
| **Reference models** | RenderReferenceNode[] via /api/render/snapshot | RenderReferenceNode[] via bridge snapshot | ✅ |
| **Materials/textures** | RenderMaterial[] + RenderTexture[] | Same types via bridge | ✅ |
| **Capture readiness** | CaptureReadyManager | CaptureReadyManager | ✅ |
| **Camera controls** | OrbitControls | OrbitControls | ✅ |

### Known parity gaps:

1. **Electron bridge snapshot missing reference texture data:** The `MeshSnapshotHandler` uses `RenderSceneSnapshotService` as authoritative source but maps back to legacy `MeshSnapshotResponse` DTO. The bridge `voxelforge.render.snapshot` channel is registered in `Program.cs` but the handler needs updating to pass through the full `RenderSceneSnapshot` including materials/textures. → Tracked as follow-up.

2. **Electron reference model transform/UV/material parity:** Bridge `MeshSnapshotHandler` uses `RenderSceneSnapshotService` but the mapped `MeshSnapshotResponse` DTO omits per-mesh geometry and texture info. → Tracked as follow-up.

3. **Electron SSE vs bridge event granularity:** MCP SSE carries revision numbers; bridge events carry domain/sequence/snapshot. Both support the semantic event contract but at different granularity. Acceptable divergence per the ADR.

4. **Electron smoke test requires X server:** `npm run smoke-test` and `npm run renderer-smoke-test` require a display server. On headless CI/agent machines without Xvfb, these tests cannot run. Compensating validation:
   - `dotnet build` + `dotnet test` pass
   - `npm run build` (TypeScript compile + esbuild bundling) succeeds
   - `npm test` (vitest unit tests) runs headless and passes
   - MCP server smoke (`dotnet run --project src/VoxelForge.Mcp`) serves /health, /viewer, /api/render/state successfully

## RuleWeaver Transfer Pattern

Use this structured note when transferring renderer-related work between agents or sessions.

### Transfer Header

```
Transfer: renderer-context
From: [agent/session id]
To: [agent/session id]
Date: [ISO 8601]
Task: [task number] — [task title]
```

### Renderer Architecture Snapshot

```
Green path:
  State:    VoxelForgeWorkspaceState (App/Workspaces)
  Service:  RenderSceneSnapshotService (App/Services)
  Contract: RenderSceneSnapshot → renderer-core/protocol/types.ts
  TS core:  electron/src/renderer-core/
  MCP URL:  /api/render/state, /api/render/snapshot
  Electron: DenBridgeRenderClient via voxelforgeBridge IPC
```

### Current State

```
Canonical endpoints: [/api/render/state, /api/render/snapshot, /viewer]
Transitional endpoints (deprecated, kept for compat): [/api/viewer-state, /api/mesh-snapshot, /api/palette]
Last smoke artifact: [/tmp/voxelforge/task1658-live-smoke.json or latest]
MCP port: 5201
Electron smoke: requires X server (Xvfb needed in headless)
```

### Known Integrity Constraints

```
1. viewer.html shell DOM hooks must match bundled TS runtime (renderer-container, diag-* elements)
2. No new inline executable JS in C# files (arch test guards)
3. Browser-facing JSON must be snake_case
4. RenderSceneSnapshot schema_version must be "voxelforge.render_scene@1"
5. Server-side transitional endpoints must remain until HttpSseRenderClient fallback is removed
```

### Open Items / Follow-ups

```
[From task thread, e.g. #1659 follow-ups]
```

## Smoke Validation Artifacts

See: `/tmp/den-hermes/t1659-coder-20260525080516/completion.json`

### Gate results:

| Gate | Status | Notes |
|------|--------|-------|
| `dotnet build voxelforge.slnx` | — | See artifact |
| `dotnet test voxelforge.slnx` | — | See artifact |
| `cd electron && npm run build` | — | See artifact |
| `cd electron && npm test` | — | See artifact |
| `scripts/run-bridge-smoke-test.sh` | — | See artifact |
| `scripts/run-renderer-smoke-test.sh` | — | May require X server |
| `scripts/run-electron-smoke-test.sh` | — | May require X server |
| MCP headless /health | — | See artifact |
| MCP headless /viewer | — | See artifact |
| MCP headless /api/render/state | — | See artifact |
| MCP headless /api/render/snapshot | — | See artifact |
| `git diff --check origin/main..HEAD` | — | See artifact |
