# Alternative TS/JS Rendering Backend Evaluation

> Task: #1201 | Follow-up from: #1181 (Task E)
> Status: exploratory evaluation (documentation only)
> Branch: `task/1201-alt-ts-js-rendering-backends`
> Base commit: `d993bff7d8877d84896862150d19469f3f7c2949`
> Last updated: 2026-05-06

## Overview

This document evaluates alternative TypeScript/JavaScript rendering backends for the VoxelForge Electron renderer path. The current implementation uses **Three.js r184** — a pragmatic first-pass choice from the Electron experiment (#1169–#1181). Before committing to deeper Three.js migration, this survey assesses whether an alternative backend materially improves packaging, performance, testability, or maintainability for voxel workloads.

### Evaluation Criteria

Each backend is assessed against the following dimensions, tied to VoxelForge's existing [`bridge-protocol.md`](bridge-protocol.md) and [`electron-renderer-experiment.md`](electron-renderer-experiment.md):

| Criterion | Weight | What We Measure |
|-----------|--------|-----------------|
| **Mesh snapshot/update fit** | Critical | How naturally the backend consumes flat vertex+index+color buffers from the C# `MeshSnapshotService`. |
| **Incremental region update support** | High | Can the backend patch individual mesh regions without full scene rebuild? |
| **Raycasting / picking** | High | Can the backend efficiently compute which voxel the pointer hits? |
| **Packaging simplicity** | Medium | Bundle size, dependency count, Electron/Node.js compatibility. |
| **Testability** | Medium | Can the backend run headless without a GPU? Are tests feasible in CI? |
| **WebGPU path** | Medium | Does the backend have a mature WebGPU renderer for future migration? |
| **Maintenance surface** | Medium | API stability, community size, documentation quality, churn risk. |
| **Voxel-specific affordances** | Low | Does the backend have built-in voxel/grid/instancing features? |

### Existing Baseline: Three.js r184

The current implementation documented in [`electron/src/renderer/scene.ts`](../../electron/src/renderer/scene.ts):

- **Mesh construction**: Consumes `MeshSnapshotData` (flat `Float32Array` positions/normals, `Uint32Array` indices, decoded RGBA vertex colors). Builds `BufferGeometry` with position/normal/color attributes and index. Uses `MeshStandardMaterial` with vertex colors.
- **Incremental updates**: `applyIncrementalUpdate()` replaces per-region `Mesh` children in a `Group` — one `BufferGeometry` + `MeshStandardMaterial` per region. Supports both `full_replace` and `incremental` update types.
- **Raycasting**: Standard `Raycaster.intersectObjects()` on mesh group children. Computes voxel position from hit point and face normal.
- **WebGPU**: Three.js r184 has experimental `WebGPURenderer` through the `three/webgpu` package export, but the current code uses `WebGLRenderer` only.
- **Testability**: The renderer runs headless (`--renderer-smoke-test`) with graceful WebGL fallback (shows text overlay). C# sidecar and TypeScript renderer logic are covered by tests, including frame parsing, byte decoding, placement math, refresh coalescing, and mesh-construction fixtures; full WebGL scene behavior is still primarily covered by smoke tests.
- **Bundle**: Three.js r184 adds ~600 KB minified to the renderer bundle. Current `dist/renderer/bundle.js` is 1.2 MB.

---

## Candidate 1: Babylon.js

### Overview

Babylon.js is a full-featured 3D engine for the web. Version 8.0 was released March 27, 2025 with native WGSL core shaders and direct WebGPU support without a GLSL-to-WGSL conversion layer ([source](https://blogs.windows.com/windowsdeveloper/2025/03/27/announcing-babylon-js-8-0/)). It is developed by Microsoft and has a large community.

### Mesh Snapshot / Update Fit

| Aspect | Assessment |
|--------|------------|
| **Buffer construction** | Babylon.js uses `VertexBuffer` with `FloatArray` data and position/normal/color/uv kind flags. The existing flat-array `MeshSnapshotData` format is trivially mappable: `new VertexBuffer(engine, data, VertexBuffer.PositionKind, 3)`. |
| **Index buffer** | `Mesh` uses indices via `setIndices()`. Direct equivalent to Three.js. |
| **Incremental region updates** | Babylon.js supports `MultiMaterial` and submeshes, which could map to per-region geometry within a single mesh. However, the current Three.js approach (separate `Mesh` per region) is also straightforward in Babylon via `Mesh.Clone()` or separate meshes in a `TransformNode` hierarchy. The incremental region replacement pattern is identical in complexity. |
| **Vertex colors** | Babylon.js `StandardMaterial` supports vertex colors with `useVertexColors = true`. Equivalent to Three.js `vertexColors` attribute. |
| **Mesh APIs** | More opinionated: meshes have built-in actions, animations, and physics integration. The extra surface area is unnecessary for voxel rendering. |

**Verdict**: Equivalent fit. The mesh snapshot format maps cleanly. No material advantage over Three.js for flat buffer consumption.

### Incremental Region Updates

Babylon.js has a stronger `MultiMaterial` / submesh system than Three.js. A single `Mesh` can have multiple `SubMesh` objects each referencing a material index. For the incremental region pipeline, this means a single mesh with per-region submeshes, avoiding the `Group` + per-region `Mesh` pattern Three.js requires.

However:
- The current Three.js pattern (one `Mesh` per region in a `Group`) works correctly and is already implemented.
- Babylon.js's submesh approach does not significantly reduce GPU draw-call overhead for the typical region count (~10–100 regions for a voxel model).
- The per-region `BufferGeometry` replacement pattern would be similar.

**Verdict**: Slight advantage for Babylon's submesh system, but not transformative for voxel workloads.

### Raycasting / Picking

Babylon.js has a built-in `Ray` and `mesh.intersects()` system that is more feature-rich than Three.js's raycaster:
- Supports world-space and local-space raycasting
- Built-in picking info with face ID, submesh ID, and barycentric coordinates
- `ActionManager` can trigger actions on pick

For VoxelForge's use case (screen-space click → ray → hit voxel), both are equivalent. The extra Babylon.js features (action managers, complex pick predicates) are not needed.

**Verdict**: Equivalent for VoxelForge's raycasting needs.

### Packaging

| Dimension | Babylon.js 8.0 | Three.js (current) |
|-----------|----------------|-------------------|
| **Minified bundle size** | ~1.2 MB (full engine) vs ~600 KB | ~600 KB (core + OrbitControls) |
| **Dependency count** | monolithic `@babylonjs/core` + optional submodules | modular `three` + `@types/three` |
| **Tree-shaking** | Limited — importing `Engine` pulls in renderer pipeline | Better — modular imports for specific features |
| **Electron compatibility** | WebGL2 preferred; WebGPUEngine creation is async ([source](https://doc.babylonjs.com/setup/support/webGPU/webGPUBreakingChanges/)) | WebGL1/2 works; no async init needed |

Babylon.js's larger full-engine bundle is a disadvantage for an Electron renderer where only a small subset of features (mesh rendering, picking, camera controls) is needed. Three.js's modular import tree naturally produces a smaller bundle.

**Verdict**: Three.js wins for packaging due to smaller bundle and better tree-shaking.

### Testability

- Babylon.js requires a GPU context for most operations, similar to Three.js.
- A headless mode exists (`engine.renderEvenIfOffscreen = true`) but is experimental.
- WebGPUEngine creation is async, complicating test setup.
- No significant testability advantage over Three.js.

**Verdict**: Equivalent to Three.js — both require GPU or graceful fallback.

### WebGPU Path

Babylon.js 8.0 ships native WGSL core shaders — a genuine advantage. The WebGPU implementation is described as "complete" in official docs ([source](https://doc.babylonjs.com/setup/support/webGPU/webGPUStatus)), with known limitations (async texture readback, no triangle fan, no Multiview/WebXR). Three.js WebGPU support is newer and less mature at r184.

**Verdict**: Babylon.js has a more mature WebGPU path. However, WebGPU is not yet a requirement for VoxelForge — the Electron path targets desktop WebGL through Chromium's implementation.

### Maintenance Surface

- **Community**: Large, Microsoft-backed, active.
- **API stability**: Good — Babylon.js has maintained backward compatibility through major versions.
- **Documentation**: Extensive with playground examples.
- **Learning curve**: Steeper than Three.js — more concepts (scene graph, action manager, animation groups, particle systems) that are irrelevant for voxel rendering.

**Verdict**: Comparable to Three.js in community size and stability. Higher cognitive overhead for the subset of features VoxelForge needs.

### Voxel-Specific Affordances

Babylon.js has:
- `SolidParticleSystem` for many small objects (not useful for merged voxel meshes)
- `InstancedMesh` for repeated geometry (useful if voxels were individual cubes, but VoxelForge uses greedy meshing)
- Built-in `GroundMesh` and grid helpers

None of these improve upon Three.js for VoxelForge's specific mesh snapshot model.

### Verdict: Babylon.js

**Recommendation: Do not adopt.** Babylon.js provides no material advantage over Three.js for VoxelForge's voxel mesh workload. The bundle is larger, the API surface is wider than needed, and the submesh/region advantage is marginal. The more mature WebGPU path is not yet a differentiator given VoxelForge's desktop Chromium target.

---

## Candidate 2: Raw WebGL / Raw WebGPU

### Overview

Using the `WebGL2RenderingContext` or `WebGPUDevice` directly, without a framework. The TS renderer would manage shaders, buffers, draw calls, and pipeline state.

### Mesh Snapshot / Update Fit

| Aspect | Assessment |
|--------|------------|
| **Buffer construction** | Trivial — upload flat arrays as `WebGLBuffer` → `gl.bufferData()`. The `MeshSnapshotData` arrays are already in GPU-friendly flat format. |
| **Index buffer** | Equivalent — `ELEMENT_ARRAY_BUFFER` with `Uint32Array`. |
| **Incremental region updates** | **Strongest fit.** Raw WebGL `bufferSubData` updates can patch individual vertex/index ranges without re-uploading the entire buffer. This maps directly to the `vertex_offset`/`vertex_count` / `index_offset`/`index_count` fields in the bridge protocol's `MeshRegionUpdateData`. |
| **Vertex colors** | Trivial — interleaved or separate attribute. |
| **Scene management** | Zero built-in — node transforms, object hierarchy, frustum culling, and LOD selection must be implemented from scratch. |

**Verdict**: Excellent buffer-level fit for the mesh snapshot/update model — the incremental region pipeline is architecturally simpler with raw WebGL because `bufferSubData` is a native operation. But the lack of scene management is a significant implementation burden.

### Incremental Region Updates

Raw WebGL offers the **optimal** incremental path:
- `gl.bufferSubData(GL_ARRAY_BUFFER, offset, newData)` patches vertex data for a specific region.
- `gl.bufferSubData(GL_ELEMENT_ARRAY_BUFFER, offset, newIndices)` patches index data.
- No new buffer allocations, no geometry construction, no material creation.

This contrasts with Three.js, where incremental updates require creating a new `BufferGeometry` per region, uploading it, and adding/removing scene graph nodes. Three.js has `BufferAttribute.needsUpdate = true` for dynamic buffers, but the region-keyed pattern (replacing entire regions) is still more allocation-heavy than raw `bufferSubData`.

For VoxelForge's region-based incremental pipeline (`MeshRegionService` produces per-region geometry), the raw WebGL approach would:
1. Pre-allocate a large vertex buffer (max expected voxel count × vertices per voxel estimate)
2. Pre-allocate an index buffer of similar size
3. On each region update, call `bufferSubData` with the region's offset and data
4. Issue a single `gl.drawElements` call with the total index count (or per-region draw calls for partial updates)

The pre-allocation challenge is non-trivial — VoxelForge models grow and shrink. Buffer growth requires reallocation and re-upload of all data, which negates the incremental benefit. A hybrid approach (chunked buffers or multiple fixed-size buffer objects per region) adds complexity.

**Verdict**: Optimal for static-mesh incremental updates with pre-allocated buffers. Pre-allocation and buffer growth strategy is a significant design challenge for voxel models that change size.

### Raycasting / Picking

Raw WebGL has no built-in raycasting. The implementer must:
1. Read the depth buffer at the cursor position (requires a separate render pass or a dedicated pick pass)
2. Unproject the screen coordinate to world space
3. Compute the ray-mesh intersection manually against the vertex/index buffers
4. Compute the voxel position from the hit point and face normal

This is ~200–300 lines of implement-your-own code compared to Three.js's one-liner `raycaster.intersectObjects()`. For voxel meshes where accuracy matters at integer coordinates, the custom implementation must handle edge cases (coplanar faces from greedy meshing, degenerate triangles, vertex snapping).

**Verdict**: Significant implementation burden. Not justified for VoxelForge's current raycasting needs.

### Packaging

| Dimension | Raw WebGL | Three.js (current) |
|-----------|-----------|-------------------|
| **Bundle size** | Zero additional dependency. The entire renderer is handwritten TS. | ~600 KB. |
| **Dependency count** | Zero. | `three` + `@types/three`. |
| **Update risk** | Browser WebGL/WebGPU APIs evolve slowly. | Three.js has ~weekly releases on npm. |

**Verdict**: Raw WebGL wins on packaging — zero dependencies, minimal bundle size.

### Testability

- Raw WebGL requires a WebGL context for any buffer/shader operation.
- `gl.getError()` and manual context validation are the debugging tools.
- Running tests headless requires `xvfb-run` or `offscreencanvas` support.
- Shader compilation errors are runtime, not compile-time.
- **No significant testability advantage** — you trade one set of framework complexities for lower-level ones.

**Verdict**: Worse than Three.js for testability. Shader validation, buffer setup, and camera math are all manually tested or runtime-debugged.

### WebGPU Path

WebGPU is an alternative raw API. For a raw approach, the implementer could target WebGPU instead of WebGL2, gaining:
- Compute shaders for voxel operations (e.g., mesh generation on GPU)
- Explicit resource management (no hidden GL state)
- Better buffer update semantics (no implicit synchronization)

However, WebGPU availability depends on the Chromium/Electron runtime and platform GPU stack, and the API/tooling is still evolving compared with WebGL2. A raw WebGPU implementation would still need a WebGL fallback for broader compatibility and for environments where WebGPU is disabled or unavailable.

**Verdict**: Raw WebGPU is the strongest performant option for the long term but requires implementing everything from scratch (including WebGL fallback), which is a project-level commitment (months of work), not a simple backend swap.

### Maintenance Surface

| Dimension | Raw WebGL | Three.js |
|-----------|-----------|----------|
| **Lines of code (renderer)** | Estimated 2,000–3,000 lines (scene graph, camera, raycaster, shaders, lights, grid) | ~800 existing lines in [`scene.ts`](../../electron/src/renderer/scene.ts) |
| **Shader investment** | Must write GLSL/HLSL/WGSL vertex+fragment shaders with lighting, shadows, vertex colors | Built-in MeshStandardMaterial with PBR |
| **Bug surface** | Each bug in camera math, raycasting, or buffer management must be debugged from first principles | Framework has battle-tested math and rendering |
| **Upgrade risk** | Very low — WebGL2 is stable and backward-compatible | Medium — API churn, deprecation cycles |

**Verdict**: Raw WebGL is high-maintenance. The framework cost (Three.js) is worth paying for camera, lighting, raycasting, and scene graph alone.

### Verdict: Raw WebGL / WebGPU

**Recommendation: Do not adopt.** While raw WebGL offers the best incremental buffer update model and zero-dependency packaging, the implementation cost for camera controls, lighting, raycasting, scene management, and (for WebGPU) WebGL fallback is prohibitive. This is a project-level rewrite, not a backend swap. VoxelForge is a voxel editor first, not a WebGL framework research project.

**Worth watching:** If the incremental mesh pipeline ever requires GPU-side fine-grained per-region buffer updates for performance, a targeted buffer-layer abstraction over raw WebGL could be added **underneath** Three.js (using `RawShaderMaterial` and manual buffer management for the mesh regions while keeping Three.js for camera/lighting/UX). This is a future optimization, not a backend replacement.

---

## Candidate 3: Canvas 2D Tile Renderer

### Overview

Using `CanvasRenderingContext2D` to render voxels as a 2D projection (orthographic or isometric) of tile images. See [Canvas Tile Engine](https://www.canvastileengine.com/) for one modern approach, and [Obelisk.js](https://github.com/nosir/obelisk.js/) for an isometric pixel voxel reference.

### Mesh Snapshot / Update Fit

This approach does **not** consume the mesh snapshot format (vertex buffers, index buffers, normals). Instead, it would need to:
1. Directly read `VoxelModel` voxel data (skipping the mesher entirely)
2. Render each visible voxel as a 2D projected tile with the palette color
3. Handle z-ordering for isometric/orthographic depth

This would require a new protocol decision because VoxelForge's current renderer contract is mesh snapshots, not raw voxel model snapshots. The [`bridge-protocol.md`](bridge-protocol.md) "Forbidden Patterns" section explicitly forbids TS from sending direct `VoxelModel` mutations; it does not literally forbid a future C#-owned raw voxel snapshot event. However, exposing raw voxel data to TS would broaden the boundary and should not be smuggled in as a renderer backend swap. The mesh snapshot is the explicit rendering contract today.

**To work within the protocol**, a 2D canvas renderer would need to:
1. Convert the mesh snapshot (vertex positions, indices, colors) to a 2D projection — which is effectively reimplementing a rasterizer in software
2. This would be **slower** than WebGL for any scene larger than a handful of voxels

**Verdict**: Poor fit. The mesh snapshot format is a 3D vertex buffer, not a 2D tile map. Using it for 2D rendering would require software rasterization, destroying performance.

### Incremental Region Updates

Software rasterization of region updates is CPU-bound. For a ~1,000 voxel edit region, re-rasterizing 16×16×16 = 4,096 voxels on CPU is significantly slower than uploading a small vertex buffer to GPU.

**Verdict**: Poor fit for incremental updates.

### Raycasting / Picking

Canvas 2D picking is feasible (use `isPointInPath()` or hit-test against a spatial index of tile rectangles), but for 3D scenes projected to 2D, the depth sorting and face-selection logic is non-trivial and must be handwritten.

**Verdict**: Worse than WebGL-based raycasting for 3D voxel scenes.

### Packaging

| Dimension | Canvas 2D | Three.js (current) |
|-----------|-----------|-------------------|
| **Bundle size** | Zero additional dependency. Native browser API. | ~600 KB. |
| **WebGL requirement** | None — works without GPU. | Requires WebGL (graceful fallback in place). |
| **Headless/CI** | Works in Node.js with `node-canvas` or without display. | Requires headless display (xvfb). |

**Best packaging story of any option** — zero deps, works everywhere.

### Testability

Canvas 2D is highly testable:
- `OffscreenCanvas` works in Node.js with `node-canvas`
- Can render to a buffer and check pixel colors
- No GPU dependency at all
- Easy to write visual regression tests

**Strongest testability story** of any option.

### WebGPU Path

Not applicable. Canvas 2D is fundamentally CPU-rendered.

### Maintenance Surface

- Minimal API surface — `CanvasRenderingContext2D` is stable and unchanging.
- No framework churn to track.
- But: implementing 3D projection, depth sorting, per-voxel face visibility, lighting simulation, and camera controls in 2D canvas is substantial code.

**Verdict**: Low dependency maintenance, high implementation maintenance.

### Voxel-Specific Affordances

- Isometric tile rendering can look attractive for voxel editors (similar to MagicaVoxel's orthographic view).
- Canvas 2D naturally handles pixel-perfect rendering at integer voxel positions.
- Tile atlas rendering is trivially efficient for repeated content.

### Verdict: Canvas 2D Tile Renderer

**Recommendation: Do not adopt as primary renderer.** The mesh snapshot protocol is a 3D vertex buffer contract; a 2D canvas renderer cannot consume it efficiently without software rasterization. Using raw voxel data would require a deliberate bridge protocol extension or a parallel 2D snapshot format, not just a renderer backend replacement.

**However, a Canvas 2D view is worth considering as an optional secondary display mode:**
- **Orthographic 2D slice view**: Render one XY/XZ/YZ slice at a time as colored tiles. Useful for pixel-art-style editing.
- **Mini-map / overview**: A small 2D top-down projection of the model.
- **Diagnostic fallback**: When WebGL is unavailable, show a 2D wireframe projection instead of a text message.

These would require exposing voxel data (not just mesh snapshots) to TS, which is a protocol-level decision. A future task could evaluate whether a C#-owned "voxel grid snapshot" event (for example, a compressed occupied-voxel list plus palette indices) is worth adding to the bridge protocol for auxiliary views without granting TS any new mutation authority.

---

## Summary Comparison

| Criterion | Three.js (current) | Babylon.js | Raw WebGL/WebGPU | Canvas 2D |
|-----------|-------------------|------------|------------------|-----------|
| **Mesh snapshot fit** | ✅ Excellent | ✅ Excellent | ✅ Optimal buffers | ❌ Requires software rasterization |
| **Incremental region updates** | ✅ Good (per-region meshes) | ✅ Good (submesh system slightly better) | ✅ Best (bufferSubData) | ❌ CPU-bound rasterization |
| **Raycasting** | ✅ Built-in | ✅ Built-in | ❌ Must implement | ❌ Must implement |
| **Packaging** | ✅ ~600 KB modular | ⚠️ ~1.2 MB monolithic | ✅ Zero deps | ✅ Zero deps |
| **Testability** | ⚠️ Needs GPU/fallback | ⚠️ Needs GPU/fallback | ❌ Manual shader/buffer testing | ✅ OffscreenCanvas |
| **WebGPU path** | ⚠️ Experimental (r184) | ✅ Mature (8.0 native WGSL) | ✅ Optimal (if implemented) | ❌ N/A |
| **Maintenance surface** | ✅ Moderate | ⚠️ Larger API than needed | ❌ High (everything from scratch) | ⚠️ 2D proj high impl. cost |
| **Scene graph / camera** | ✅ Built-in | ✅ Built-in | ❌ Must implement | ❌ Must implement |
| **Lighting** | ✅ Built-in PBR | ✅ Built-in PBR | ❌ Must write shaders | ❌ Must simulate |
| **Voxel-specific affordances** | ❌ None needed | ❌ SolidParticleSystem (not useful) | ❌ None | ⚠️ Isometric tile heritage |

---

## Recommendation

### Keep Three.js as the Primary Electron Renderer Backend

No alternative materially improves upon Three.js for VoxelForge's current renderer needs. The evaluation finds:

1. **Babylon.js**: Comparable in all relevant dimensions but with a larger bundle and wider API surface. No compelling reason to switch.
2. **Raw WebGL/WebGPU**: The best incremental buffer update model, but the implementation cost for camera, lighting, raycasting, and scene management is project-level — not a backend swap. Worth monitoring for future targeted optimizations (buffer-layer abstraction under Three.js), not as a replacement.
3. **Canvas 2D**: Architecturally incompatible with the mesh snapshot protocol. Worth considering only as a secondary orthographic/diagnostic view, not as a primary renderer.

### Where Three.js Is Adequate (and No Alternative Is Better)

| Requirement | Why Three.js Is Sufficient |
|-------------|---------------------------|
| **Mesh snapshot consumption** | Direct `BufferGeometry` construction from flat arrays — trivial and correct. |
| **Incremental region updates** | Per-region `Mesh` in `Group` works correctly. Draw-call overhead is acceptable for typical region counts (~10–100). |
| **Raycasting** | `Raycaster` with `intersectObjects()` produces correct hit positions and normals. The voxel-snapping logic is already implemented and tested. |
| **Lighting** | `MeshStandardMaterial` with PBR provides good default appearance. No shader writing needed. |
| **Camera controls** | `OrbitControls` handles pan/zoom/orbit with damping. Already implemented and working. |
| **Grid helper** | `GridHelper` is built-in. |
| **Testability** | Graceful WebGL fallback mode already implemented. Scene construction runs in smoke tests without GPU. |

### Targeted Optimization Opportunities (Not Backend Replacements)

If the incremental mesh pipeline becomes a performance bottleneck, the most pragmatic improvement is to optimize **within Three.js** rather than switching backends:

1. **`BufferAttribute.needsUpdate` + pre-allocated buffers**: Instead of creating new `BufferGeometry` per region, pre-allocate large buffers and use `needsUpdate = true` with `bufferSubData`-style uploads. Three.js supports this through `BufferAttribute.copyArray()` and `attribute.needsUpdate = true`.
2. **`RawShaderMaterial` for voxels**: Replace `MeshStandardMaterial` with a custom shader that handles vertex colors and simple lighting. This reduces shader complexity and draw-call overhead.
3. **Merged geometry with per-vertex region IDs**: Maintain one merged `BufferGeometry` with a per-vertex attribute encoding the region ID. On incremental update, patch the relevant vertex/index ranges in-place using `BufferAttribute.set()` and `needsUpdate = true`. This avoids per-region scene graph nodes entirely.
4. **`StaticDrawUsage` → `DynamicDrawUsage`**: The current implementation uses default draw usage (static). Switching to `DynamicDrawUsage` for the mesh geometry tells WebGL to optimize for frequent buffer updates.

These optimizations are all achievable within the existing Three.js codebase. They are follow-up work for a dedicated optimization task, not requirements for the current implementation.

### WebGPU: Watch, Don't Ship

Both Three.js (experimental in r184) and Babylon.js (mature in 8.0) offer WebGPU. VoxelForge runs in Electron (Chromium 134+) which supports WebGPU. However:

- The current WebGL-based Three.js implementation works correctly.
- WebGPU would require a separate rendering path maintenance burden (two paths or a conditional switch).
- The mesh snapshot / incremental update model is API-agnostic — the GPU backend choice is orthogonal to the bridge protocol.
- Edge cases (WebGPUEngine async creation, texture readback, stricter shader validation) add complexity without immediate benefit.

**Revisit WebGPU when:**
- Three.js WebGPU support reaches stable (non-experimental) status at the feature level VoxelForge uses.
- A performance benchmark shows WebGPU materially improves frame time for voxel scenes (>20% improvement at 100k+ voxels).
- A task explicitly evaluates a WebGPU render path as an additional (not replacement) backend.

To avoid forgetting this decision, add a lightweight renderer-backend check to the next roadmap/decision checkpoint rather than tracking WebGPU status continuously in this task.

### Canvas 2D Diagnostic View (Future Option)

A Canvas 2D orthographic slice view could be useful as a secondary diagnostic/mini-map display. This would require:

1. A new bridge protocol message type — `voxelforge.voxel.grid_snapshot` — that sends a flat compressed snapshot of occupied voxel positions and palette indices.
2. A protocol decision on whether TS may receive raw voxel grid data (currently forbidden by architecture rules).
3. Implementation of the 2D projection, zoom, and picking in a separate TS module.

This is a low-priority feature, not a backend replacement. If pursued, it would be a separate task.

---

## Cross-References

- [`electron-renderer-experiment.md`](electron-renderer-experiment.md) — Original architecture plan with boundary rules.
- [`bridge-protocol.md`](bridge-protocol.md) — Mesh snapshot/update protocol that renders consume.
- [`electron-renderer-decision-checkpoint.md`](electron-renderer-decision-checkpoint.md) — Prior decision checkpoint and Task E scope.
- [`electron/src/renderer/scene.ts`](../../electron/src/renderer/scene.ts) — Current Three.js scene implementation.
- [`electron/src/renderer/index.ts`](../../electron/src/renderer/index.ts) — Current renderer interaction logic.
- [`electron/package.json`](../../electron/package.json) — Current dependency list (Three.js r184).
- [`docs/benchmarks/renderer-benchmark-summary.md`](../benchmarks/renderer-benchmark-summary.md) — Current benchmark evidence.
- [`docs/architecture/fna-incremental-mesh-backport-evaluation.md`](fna-incremental-mesh-backport-evaluation.md) — Incremental pipeline assessment.

### External Sources

- Babylon.js WebGPU status: https://doc.babylonjs.com/setup/support/webGPU/webGPUStatus
- Babylon.js 8.0 release notes (native WGSL): https://blogs.windows.com/windowsdeveloper/2025/03/27/announcing-babylon-js-8-0/
- Babylon.js WebGPU breaking changes: https://doc.babylonjs.com/setup/support/webGPU/webGPUBreakingChanges/
- Canvas Tile Engine: https://www.canvastileengine.com/
- Obelisk.js (isometric pixel voxels): https://github.com/nosir/obelisk.js/
