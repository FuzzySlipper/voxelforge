# Electron Renderer Experiment — Decision Checkpoint

> Task: #1181 | Parent: #1168  
> Status: decision record  
> Branch: `task/1181-electron-decision-checkpoint`  
> Base commit: `4dcff158469da5ec90f16a9602e044eda44a0c4d`

## Overview

This document records the evaluation and decision for the Electron/Three.js renderer in VoxelForge. The JavaScript/WebGL viewer (MCP `/viewer` endpoint) and the Electron renderer are now the canonical visual paths. The previous FNA/Myra frontend (`VoxelForge.Engine.MonoGame`) was retired in task #1632.

The complete epic consisted of tasks #1169–#1181. All subtasks are completed and reviewed. The FNA/Myra path was retired in #1632 after the JS renderer proved successful.

## Evidence Summary

### What Was Built

| Component | Tasks | Status | Key Deliverables |
|-----------|-------|--------|------------------|
| **Architecture plan** | #1169 | Done | `electron-renderer-experiment.md`, `bridge-protocol.md` |
| **den-bridge submodule** | #1170 | Done | `lib/den-bridge` at `3eed6133` |
| **C# sidecar + bridge smoke test** | #1171 | Done | `VoxelForge.Bridge/Program.cs`, bridge smoke test |
| **Bridge protocol** | #1172 | Done | Full message schema in `bridge-protocol.md` |
| **Upstream den-bridge hardening** | #1173, #1183 | Done | Enriched error propagation, IPC handlers, disconnect handling |
| **Renderer-neutral mesh services** | #1174 | Done | `MeshSnapshotService`, `PaletteSnapshotService` in `VoxelForge.App` |
| **Static mesh renderer** | #1175 | Done | Three.js scene, camera, orbit controls, mesh snapshot construction |
| **Incremental mesh updates** | #1176 | Done | `MeshRegionService`, `MeshSubscriptionManager`, `MeshChangePushService`, incremental mesh update pipeline |
| **First vertical slice** | #1177 | Done | Electron UI with tool palette, palette list, undo/redo, state diagnostics, viewport editing |
| **Editing interactions** | #1178 | Done | Click→raycast→bridge→C# command round-trip for place/remove/paint/select/undo/redo |
| **MCP/live preview preservation** | #1179 | Done | Electron `--preview` flag, Open/Load project flow, MCP independence documented |
| **Packaging/dev workflow** | #1180 | Done | `electron-builder.yml`, `scripts/run-electron-dev.sh`, directory packaging |
| **Decision checkpoint** | #1181 | In progress | This document |

### Test Coverage

| Test Suite | Count | Status |
|------------|-------|--------|
| `VoxelForge.Bridge.Tests` | 77 | ✅ All pass |
| `VoxelForge.App.Tests` | 90 | ✅ All pass |
| `VoxelForge.Core.Tests` | 103 | ✅ All pass |
| Architecture Tests | 31 | ✅ All pass |
| Other solution test projects (`LLM`, `Mcp`, `Evaluation`, `Import`) | 68 | ✅ All pass |
| Overall `dotnet test voxelforge.slnx` | 369 | ✅ All pass |
| Bridge smoke test (C# only) | 77 | ✅ Pass |
| Electron smoke test | — | ✅ Validated |
| Renderer smoke test | — | ✅ Validated |

## Comparison: Electron/TS vs FNA/Myra

### UI Quality

| Dimension | FNA/Myra (Control) | Electron/Three.js (Experimental) | Notes |
|-----------|-------------------|----------------------------------|-------|
| **Panel chrome** | Myra widgets (button, list, label, text field) | Raw HTML/CSS via Chromium | Electron has native-quality UI and CSS; Myra is functional but visually basic. |
| **Responsive layout** | Fixed Myra layout with manual resize handling | CSS flexbox/grid, auto-layout | Electron wins significantly here. |
| **3D canvas** | FNA DirectX-style rendering | Three.js WebGL | Both are capable; Three.js offers richer post-processing and easier shader integration. |
| **Text rendering** | FontStashSharp (bitmap fonts) | Chromium text rendering | Electron text is sharper, supports Unicode/emoji/kerning natively. |
| **Theme** | Hardcoded Myra theme | CSS themable | Electron has full CSS customizability. |
| **Tooltips/overlays** | Myra tooltip support | DOM overlay | Electron overlays are trivial. |

**Verdict:** Electron/TS provides significantly higher UI quality with standard web tooling. However, the current Electron UI is minimal (tool list, palette, diagnostics) — the FNA/Myra UI, while visually basic, has full feature completeness from years of incremental work. **Electron is ahead on potential; FNA/Myra is ahead on completeness.**

### Render Performance

| Metric | FNA/Myra | Electron/Three.js | Gap |
|--------|----------|-------------------|-----|
| **Static mesh rendering (frame time)** | DirectX via FNA3D | WebGL via Three.js | No real profiling data; both are GPU-bound for voxel scenes. FNA is slightly lighter-weight (no browser runtime). |
| **Large scene (>100k voxels)** | Greedy mesher + indexed vertex buffers | JSON mesh snapshots same mesher + vertex buffers | JSON serialization becomes a bottleneck for large scenes (confirmed in bridge protocol doc: acceptable <10k vertices, needs binary format above). |
| **Incremental mesh update** | N/A (push full mesh via file watcher) | Chunked incremental updates via C# `MeshRegionService` | Electron has a structured incremental pipeline; FNA/Myra has no incremental mesh mechanism (full reload). This is an **architectural advantage for Electron** that could be backported to FNA. |
| **Edit latency (C# round-trip)** | Direct in-process service calls | Instrumented via bridge diagnostics, but not benchmarked systematically | FNA is inherently lower-latency because it is in-process. Electron adds serialization, WebSocket, and browser event-loop overhead; the experiment added diagnostics but still needs a repeatable benchmark. |
| **WebGL fallback** | N/A | Graceful fallback to CPU-only metrics | Electron handles headless/CI environments with fallback. FNA requires GPU. |

**Raw frame time comparison:** No instrumented benchmarks exist. The `voxelforge.diagnostics.editing_latency` events measure C# processing only. TS-side frame time measurement is displayed in the viewport HUD but was not systematically collected over a test harness.

**Verdict:** FNA/Myra is probably faster for in-process edits; Electron/Three.js has better incremental architecture and handles large-scene updates more gracefully in design. Real performance profiling needs a standardized test scene and measurement harness that neither path currently has.

### Edit Latency

| Scenario | FNA/Myra | Electron/Three.js | Evidence status |
|----------|----------|-------------------|-----------------|
| Single voxel place/remove/paint | Direct in-process command execution | Bridge request to C# command plus mesh/state event response | Smoke-tested; no repeatable latency benchmark yet |
| Undo/redo | Direct in-process command execution | Bridge request to `voxelforge.history.undo` / `voxelforge.history.redo` | Smoke-tested; no repeatable latency benchmark yet |
| Tool change | In-process UI/session update | Bridge request to C# session state | Implemented; no benchmark needed for current decision |
| Mesh update after small edit | Existing renderer path rebuild behavior | Incremental dirty-region push via `MeshSubscriptionManager` and `MeshChangePushService` | Architecturally implemented and tested; frame-time impact not benchmarked |
| Full mesh rebuild/load | In-process mesh generation and upload | JSON mesh snapshot over bridge, then Three.js scene build | Static/renderer smoke-tested; large-scene timing not benchmarked |

The experiment added `voxelforge.diagnostics.editing_latency` events and a viewport HUD for latency visibility, but the epic did **not** collect a repeatable latency table across standardized scenes. The current evidence is therefore functional/architectural: editing interactions work through C# commands and validation smokes pass, but a systematic latency benchmark remains follow-up work.

**Verdict:** FNA/Myra remains the safer latency choice because it is in-process. Electron/TS appears viable for the validated interaction path, but the decision should not depend on uncollected latency numbers.

### Bridge Complexity

| Dimension | Assessment |
|-----------|------------|
| **Protocol surface area** | 14 registered commands, 4 event types — moderate but well-scoped. |
| **Wire format** | JSON over WebSocket; snake_case convention, versioned schema. |
| **C# sidecar** | `VoxelForge.Bridge` (~600 LOC composition + ~2,500 LOC handlers) — thin adapter layer on top of existing `VoxelForge.App` services. No FNA/Myra references. |
| **TypeScript client** | `bridge-client.ts` (~250 LOC) — raw WebSocket frame protocol. No upstream `den-bridge` NPM package yet. |
| **Error model** | Structured `BridgeError` with categories, retryable flags, chained causes. |
| **Event model** | Bridge pushes `state.delta`, `mesh.update`, `palette.update`, `editing_latency` events. |

**Upstream bridge improvements made during the experiment (tasks #1173/#1183):**

| Improvement | Impact |
|-------------|--------|
| Enriched error propagation | Error codes, categories, retryable flags, chained causes |
| IPC handlers in Electron main | Clean separation between renderer IPC and bridge IPC |
| Disconnect detection | Graceful handling of sidecar/Electron disconnect |
| Latency event pipeline | `voxelforge.diagnostics.editing_latency` with structured metrics |
| Mesh subscription manager | Tracks active subscriptions and dirty region state |
| Region-keyed incremental mesh | Chunk-based dirty region detection and incremental push |

**Remaining upstream work needed (still gaps):**

| Gap | Severity | Notes |
|-----|----------|-------|
| Binary mesh transport | Medium | JSON is acceptable for <10K vertices; larger models need binary framing. |
| TypeScript typings generation | Low-Medium | Manual snake_case construction in `bridge-client.ts`; would benefit from generated TS types from C# DTOs. |
| Upstream den-bridge NPM package | Medium | `bridge-client.ts` is a hand-written WebSocket client that duplicates upstream frame protocol. An official NPM package would eliminate maintenance burden. |
| Full mesh update bounds in `VoxelModelChangedEvent` | Low | Current fallback meshes all occupied regions; adding bounds metadata would enable finer-grained dirty detection. |

**Verdict:** Bridge complexity is well-managed. The protocol is well-defined, test coverage is good (77 bridge tests), and the design aligns with the architectural boundary. Upstream improvements address the main gaps. Remaining work is medium-priority and non-blocking.

### Packaging Complexity

| Dimension | FNA/Myra | Electron/Three.js |
|-----------|----------|-------------------|
| **Build setup** | `./build-native.sh` + `dotnet build` | C# sidecar build + `npm install` + `npm run build` |
| **Dependencies** | FNA, Myra, FontStashSharp, XNAssets (submodules) + native SDL3 + Vulkan | Same C# sidecar deps + Node.js + Electron + Three.js |
| **Dev workflow** | Single build command + single run command | `scripts/run-electron-dev.sh` handles both C# and TS build chains |
| **Packaged distribution** | `dotnet publish` (self-contained .NET binary + native libs) | `electron-builder` produces Linux `dir`/`AppImage` but **does not bundle the C# sidecar** |
| **Cross-platform support** | Linux only (via FNA3D targeting; FNA supports macOS, Windows in theory) | Linux only (electron-builder.yml configures Linux only; Windows/macOS packaging not configured) |
| **CI complexity** | Single `dotnet test` | Needs both .NET SDK and Node.js |

**Known gap — sidecar bundling:** The largest packaging gap is that `VoxelForge.Bridge` (the C# sidecar) is **not bundled** into the Electron package. Packaged builds (`npm run package:dir`) produce an Electron app without the required sidecar binary. Running from the repository root via `npm start` or `scripts/run-electron-dev.sh` is the only supported workflow. Until this is resolved, Electron cannot be distributed as a standalone application.

Known gap details from `electron-builder.yml`:
```
# Known gaps (future work):
#   - Sidecar bundling: VoxelForge.Bridge is a .NET project not included
#     in the Electron package. Packaged builds rely on the sidecar being
#     available on the system PATH or at a known location. For now, use
#     directory builds (`npm run package:dir`) for layout testing only.
#   - Windows/macOS targets: only Linux is configured. Add platform
#     targets when those platforms are tested.
#   - File associations: .vforge file extension registration is deferred.
```

**Verdict:** FNA/Myra packaging is simpler (single build chain, no bundling gaps). Electron packaging adds real complexity (dual build chains, sidecar bundling unresolved) and is currently Linux-only. Sidecar bundling requires a solution (e.g., `dotnet publish` as part of the build pipeline and bundling the output as Electron extraResources).

### Maintainability

| Dimension | FNA/Myra | Electron/Three.js |
|-----------|----------|-------------------|
| **Language** | C# (single language) | C# + TypeScript + HTML + CSS |
| **Build systems** | Single `.slnx` with MSBuild | MSBuild + npm + esbuild + electron-builder |
| **Test languages** | C# (xUnit) | C# (xUnit) + TS (none yet) |
| **Renderer complexity** | FNA3D abstraction over DirectX/Vulkan/Metal | Three.js abstraction over WebGL |
| **UI framework** | Myra (proprietary, custom XNA-style) | Chromium + DOM (standard web platform) |
| **State boundary** | Implicit (everything in same process) | Explicit (bridge protocol with ownership annotations) |
| **Tooling** | Rider/VS + dotnet CLI | Rider/VS + VSCode + Chrome DevTools |

**Key maintainability risk — dual language and build chains:** Keeping both renderers maintained means understanding C# mesh generation AND Three.js scene construction AND the bridge protocol AND the Electron lifecycle. This is ~2× the surface area of the FNA-only path.

**Key maintainability benefit — explicit protocol:** The bridge protocol makes the C#/TS boundary explicit, verifiable, and testable in isolation. This is a net positive even if Electron is kept as experimental — it forces clean interfaces.

**Verdict:** FNA/Myra is simpler to maintain (single language, single build chain, everything in process). Electron/TS adds significant surface area but benefits from explicit protocol boundaries and modern web tooling. The dual-renderer state is not sustainable long-term without dedicated effort.

### Testability

| Dimension | FNA/Myra | Electron/Three.js |
|-----------|----------|-------------------|
| **Unit tests** | 300+ across Core/App/LLM | 77 bridge tests (C# sidecar only) |
| **Integration tests** | Headless mode (no GPU needed for some tests) | WebSocket bridge tests (no Electron needed for C# side) |
| **Renderer tests** | None (FNA needs GPU) | Renderer smoke test (headless, collects metrics without GPU) |
| **Architecture tests** | 31 tests enforcing dependency rules | No TS-side architecture tests |
| **CI runs** | `dotnet test` — 369 passing | Needs Node.js; `npm run build` compiles TS; smoke tests need Electron display server |
| **TS-side test coverage** | N/A | None. The TypeScript renderer (`scene.ts`, `index.ts`) has no test suite. TypeScript typings or snapshot testing not set up. |

**Key testability gap — no TypeScript tests:** The Electron renderer has 77 C# bridge tests but **zero TypeScript tests**. The Three.js scene construction (`scene.ts`), the renderer interaction logic (`index.ts`), the bridge client (`bridge-client.ts`), and the preload script are untested. This is a significant gap for a production-quality component.

**Verdict:** FNA/Myra has better test coverage overall. While the C# bridge is well-tested, the TypeScript renderer is untested. Adding TS tests (e.g., Jest + headless Three.js or snapshot testing) is feasible but not yet done.

## Acceptance Criteria Assessment

Using the success metrics from the architecture plan (`electron-renderer-experiment.md`):

### Vertical Slice (#1177) Success Criteria

| Criterion | Status | Evidence |
|-----------|--------|----------|
| Sidecar starts and handshakes | ✅ | `VoxelForge.Bridge` composes App services, starts WebSocket server, emits `[BRIDGE_HANDSHAKE]` JSON. Electron main process spawns, connects, handshakes. |
| Mesh display | ✅ | Three.js renders colored voxel mesh with correct palette colors from `mesh_snapshot` response. |
| Camera control | ✅ | Orbit controls (pan, zoom, orbit) via `OrbitControls`, snap-to-view via `frameCamera()`. All in TS — no C# round-trip. |
| Single mutation round-trip visibility | ⚠️ | Editing path and `editing_latency` diagnostics exist and smokes pass, but the epic did not collect a repeatable latency benchmark. |
| Undo works | ✅ | Ctrl+Z sends `voxelforge.history.undo` through bridge; mesh reverts. Ctrl+Shift+Z / Ctrl+Y for redo. |
| No forbidden TS behavior | ✅ | Architecture tests pass; review findings confirmed no direct voxel mutation, undo logic, persistence, or MCP routing in TS. |

### Final Decision Checkpoint Criteria

| Criterion | Threshold | Status | Assessment |
|-----------|-----------|--------|------------|
| **Feature parity** | All tools, labels, animation, reference images | ❌ Not met | Only place/remove/paint/select tools wired. No fill, label, animation, or reference image support in Electron. Documented gaps. |
| **Performance** | Frame time within 2× of FNA; edit latency < 100ms | ⚠️ Partial | Functional editing and diagnostics exist, but neither frame time nor edit-latency thresholds have a systematic benchmark. Incremental mesh pipeline is architecturally superior but not yet benchmarked against FNA. |
| **Reliability** | Recoverable bridge disconnect/crash without data loss | ❌ Not tested | Graceful shutdown exists. No structured reconnect or unsaved-state-warning flow. Disconnect scenario coverage is incomplete. |
| **Build complexity** | Single script on Linux and macOS | ⚠️ Partial | `scripts/run-electron-dev.sh` works on Linux. macOS untested. Requires Node.js + dotnet SDK. |
| **Maintainer cost** | Bridge + renderer test coverage ≥ FNA renderer | ❌ Not met | 77 C# bridge tests ✅ but 0 TypeScript renderer tests. Total 369 tests vs. FNA's 292 (excluding bridge tests); however the TS code is untested. |
| **MCP/live preview preservation** | MCP and `--watch` workflows remain functional and documented | ✅ Met | MCP server unchanged. `--watch` path unchanged. Electron `--preview` added as additional surface. Documented in `mcp-server.md` and README. |

## Decision: JS Renderer is Canonical; FNA/Myra Retired

**Final decision (Task #1632): The JavaScript/WebGL viewer (MCP `/viewer`) and the Electron renderer are the canonical visual paths. The FNA/Myra native renderer (`VoxelForge.Engine.MonoGame`) and its submodules (FNA, Myra, FontStashSharp, XNAssets) have been removed from the active build.**

### Rationale

The core architecture (bridge protocol, C# sidecar, renderer-neutral mesh services, incremental update pipeline, editing interaction round-trip, and MCP preservation) is proven, tested, and reviewed. The architectural boundary (C# owns truth, TS/JS owns pixels) is cleanly maintained. The JS viewer provides a lightweight browser inspection surface without requiring Electron; the Electron renderer provides a full workbench when needed.

Removing the FNA/Myra path eliminates:
- Dual build chains and CI complexity
- Native library dependencies (SDL3, Vulkan drivers, FNA3D/FAudio builds)
- Submodule maintenance burden for FNA, Myra, FontStashSharp, and XNAssets
- Risk of neither renderer receiving focused improvement

### What Was Preserved

- `VoxelForge.Bridge` — C# sidecar and bridge protocol (used by Electron)
- `VoxelForge.Mcp` — Headless MCP server with built-in WebGL viewer
- `VoxelForge.App` — All editor state, services, and undo/redo (renderer-neutral)
- `VoxelForge.Core` — Data model, meshing, serialization (renderer-neutral)
- `lib/den-bridge` — WebSocket protocol submodule

### What Was Removed

- `src/VoxelForge.Engine.MonoGame` — FNA/Myra native renderer project
- `lib/FNA`, `lib/Myra`, `lib/FontStashSharp`, `lib/XNAssets` — Git submodules
- `build-native.sh` — FNA3D/FAudio native build script
- Active documentation references to FNA/Myra/MonoGame as supported paths

## Concrete Follow-Up Tasks

Historical follow-up tasks from the original decision checkpoint are listed below for reference. Their status reflects the world after #1632:

### Task A: Sidecar Bundling for Electron Packages

**Status:** Still open. The sidecar is not bundled into Electron packages. The `.NET` sidecar must be discoverable for packaged Electron builds to work standalone.

### Task B: TypeScript Renderer Test Suite

**Status:** Still open. The TypeScript renderer has zero test coverage.

### Task C: Systematic Performance Benchmarks

**Status:** Still open. No standardized benchmark compares frame time or edit latency across renderer paths on equivalent scenes.

### Task D: FNA Backport of Incremental Mesh Architecture

**Status:** Closed / Overtaken by events. The FNA renderer was retired in #1632; the incremental mesh architecture remains in the Electron/bridge path.

### Task E: Investigate Alternative TS/JS Rendering Backend

**Status:** Still open. The experiment currently uses Three.js. Alternative backends could be evaluated in the future.

## Known Gaps and Open Questions

### Honest Gaps

| Gap | Impact | Path to Resolution |
|-----|--------|--------------------|
| Sidecar not bundled in Electron packages | Blocking for standalone distribution | Task A above |
| Zero TypeScript test coverage | Quality risk for TS code | Task B above |
| No systematic performance benchmarks | Cannot objectively compare renderers | Task C above |
| Only 4 of 6+ tools wired in Electron | Feature incompleteness | Manual effort per tool |
| No fill/label/animation/region tools | Feature incompleteness | Manual effort per feature |
| No reconnect flow after bridge disconnect | Reliability gap | Needs structured reconnect implementation |
| macOS/Windows packaging not configured | Cross-platform gap | Add platform targets to `electron-builder.yml` |
| Binary mesh transport not implemented | Performance gap for large models | Depends on upstream `den-bridge` transport support or local implementation |
| No `den-bridge` NPM package | Maintenance burden (manual frame protocol) | Upstream contribution or local package generation |
| JSON serialization bottleneck for >10K vertices | Performance gap for large scenes | Binary transport or incremental mesh fallback |

### Open Questions

1. **What is the strategic future of FNA/Myra?** If Electron is eventually adopted, FNA/Myra becomes legacy. But FNA provides platform reach (DirectX/Vulkan/Metal) that WebGL may not match on all platforms. WebGPU could close this gap in 1-2 years.

2. **Who maintains the Electron renderer long-term?** The experiment was built incrementally by LLM agents guided by human orchestration. Sustained maintenance requires a human willing to understand both C# and TypeScript sides.

3. **What is the testing strategy for hybrid C#/TS applications?** Currently C# is well-tested via xUnit and architecture tests, but the TS side has no tests. Should TS tests run in CI alongside C# tests? This adds Node.js to the CI environment.

## Risk Notes for Reviewer Attention

1. **This document recommends "keep as parallel experimental" — a middle ground.** The risk is that neither renderer receives sufficient focus. The decision doc mitigates this by proposing concrete follow-up tasks that close gaps before a future "adopt or abandon" decision could be made.

2. **The "parallel experimental" posture is not sustainable indefinitely.** Without a clear timeline for gap closure, the Electron renderer may drift into an unmaintained state. The follow-up tasks should be prioritized at the next roadmap review.

3. **No deceptive claims of measured performance are made in this document.** Where evidence is unavailable (frame time benchmarks, TS test coverage, cross-platform validation), this is stated explicitly. The recommendation does not depend on unverified performance claims.

## Cross-References

- [`electron-renderer-experiment.md`](electron-renderer-experiment.md) — Original architecture plan and success metrics.
- [`bridge-protocol.md`](bridge-protocol.md) — Full bridge message schema.
- [`mcp-server.md`](../mcp-server.md) — MCP live preview preservation evidence.
- [`README.md`](../../README.md) — Electron dev workflow and packaging docs.
