# Renderer Performance Benchmark Summary

> Task: #1199 | Follow-up from: #1181  
> Last updated: 2026-05-06

## Overview

This document describes the systematic renderer performance benchmark workflow for VoxelForge. It was created as follow-up Task C from the Electron renderer decision checkpoint (#1181). The benchmark provides measured data for comparing renderer paths, replacing unverified performance claims with repeatable metrics.

## Reference Scenes

Four deterministic reference scenes are defined in `src/VoxelForge.Core/Benchmarking/RendererBenchmarkScenes.cs`:

| Scene ID | Voxels | Bounds | Description |
|----------|--------|--------|-------------|
| `SmallHollowCube` | 488 | 10×10×10 | Tiny hollow box — fast mesher baseline |
| `MediumHollowWithPillars` | ~3k | 22×22×22 | Hollow box with 4 internal pillars and cross-bracing |
| `LargeGridRoom` | ~26k | 48×48×48 | Large hollow room with 6 internal walls (doorways) and 9 pillars |
| `ExtraLargeCheckerboard` | ~131k | 64×64×64 | Dense checkerboard pattern (opt-in; too expensive for CI default) |

All scenes are:
- **Deterministic**: identical output for every call
- **Renderer-neutral**: no Three.js/WebGL dependency
- **Lightweight constructors**: building a scene creates a `VoxelModel` in <1ms typically

## How to Run

### C# Benchmark (Primary — fully headless, CI-friendly)

```bash
# Run default scenes (3 scenes, 5 measurement trials)
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark

# Run with extra-large scene
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark --extralarge

# Customize warmup/trials
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark --warmup 2 --trials 10

# Show help
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark --help
```

Output: JSON to stdout with per-scene metrics for GreedyMesher, NaiveMesher, MeshSnapshot, and edit mutation latency.

### Wrapper Script

```bash
# C# benchmark only (headless)
./scripts/run-renderer-benchmark.sh

# C# benchmark + Electron smoke test (requires display or Xvfb)
./scripts/run-renderer-benchmark.sh --electron

# With extra-large scene and custom output directory
./scripts/run-renderer-benchmark.sh --extralarge --output /tmp/voxelforge/my-benchmark
```

The script produces:
- `<output>/csharp-benchmark.json` — C# metrics
- `<output>/electron-smoke.json` — Electron metrics (if `--electron` used)
- `<output>/combined-summary.md` — Human-readable markdown summary

## Measured Metrics

### C# Side (always measured)

| Metric | Component | What It Measures |
|--------|-----------|-----------------|
| **GreedyMesher time (ms)** | `GreedyMesher.Build()` | Production mesher — merges adjacent coplanar faces into quads. Both renderers share this mesher, so it's a baseline for both. |
| **NaiveMesher time (ms)** | `NaiveMesher.Build()` | Debug mesher — one quad per exposed face. Baseline reference (no merging). |
| **Mesh snapshot time (ms)** | `MeshSnapshotService.BuildSnapshot()` | GreedyMesher + flat buffer construction. This is the full C# side of the Electron renderer's snapshot preparation. |
| **Edit + remesh time (ms)** | Clone + mutate + remesh | Simulates a single-voxel editing round-trip: mutate model, rebuild mesh. |
| **Estimated JSON bytes** | Heuristic | Rough size of the JSON payload if this snapshot were transmitted over the bridge to Electron. |

### Electron Side (optional, requires display)

Measured via the existing `--renderer-smoke-test` path:

| Metric | Component | What It Measures |
|--------|-----------|-----------------|
| **scene_construction_ms** | `VoxelForgeScene.buildMeshFromSnapshot()` | Three.js buffer geometry construction from snapshot data |
| **first_render_ms** | `WebGLRenderer.render()` | First GPU draw call after scene construction |
| **total_renderer_ms** | Combined | End-to-end time from snapshot receipt to rendered frame |

> **Current limitation:** The Electron smoke test uses a default empty model. Per-scene Electron benchmarks require extending the bridge protocol (voxelforge.benchmark.* commands or similar) to create specific scenes on the C# sidecar.

## Sample Results

Below are representative results from a development machine.

```
┌─────────────────────┬─────────┬──────────┬──────┬──────────┬──────┬───────────┬──────────┬──────────────┐
│ Scene               │ Voxels  │ Greedy   │ Tris │ Naive    │ Tris │ Snapshot  │ Est JSON │ Edit+Remesh │
│                     │         │ (ms)     │      │ (ms)     │      │ (ms)      │ (bytes)  │ (ms)         │
├─────────────────────┼─────────┼──────────┼──────┼──────────┼──────┼───────────┼──────────┼──────────────┤
│ SmallHollowCube     │   488   │   1      │  24  │   0      │ 1968 │    1      │   5,248  │     1        │
│ MediumHollowW/Pillar│   ~3k   │  10      │ 116  │   3      │ 12k  │    9      │  24,384  │     9        │
│ LargeGridRoom       │  ~26k   │  39      │ 890  │  11      │ 80k  │   40      │ 185,376  │    47        │
└─────────────────────┴─────────┴──────────┴──────┴──────────┴──────┴───────────┴──────────┴──────────────┘
```

> **Note:** These numbers are from a development build with the `fake` completion provider (no LLM). Absolute values depend on the machine. The **ratios** between meshers and scenes are the meaningful comparison.

## Limitations

1. **CPU-side only:** C# benchmarks measure mesh generation, snapshot construction, and model mutation — all CPU work. GPU upload, draw-call, and frame-time measurements require a full rendering framework (WebGL/Electron) and are not covered.
2. **No per-scene Electron metrics yet:** The Electron smoke test uses C#'s default initial model. To measure per-scene Electron scene construction, a bridge extension is needed (e.g., `voxelforge.benchmark.create_scene` command).
3. **JSON size estimate is rough:** The heuristic simply multiplies array element counts by estimated per-element JSON bytes. Actual serialized size depends on the `System.Text.Json` serializer settings.
4. **Does not measure incremental mesh updates:** The incremental mesh pipeline (`MeshRegionService`, `MeshChangePushService`) is architecturally important for Electron but is not benchmarked here. Adding that measurement would require creating a model, editing it, and timing the incremental push.
5. **Electron smoke test needs a display:** Even with `--disable-gpu`, Electron needs either a real display server or `xvfb-run` for the smoke test to complete. The C# benchmark is fully headless.

## CI Integration

The C# benchmark does not require GPU or display and can run in CI:

```bash
# Quick smoke: single trial, default scenes, verify JSON output
dotnet run --project src/VoxelForge.Evaluation -- renderer-benchmark --warmup 0 --trials 1 > /tmp/voxelforge/ci-benchmark.json

# Validate JSON is well-formed
python3 -c "import json; json.load(open('/tmp/voxelforge/ci-benchmark.json')); print('OK')"
```

The benchmark is **not** gated on normal `dotnet test` — it is a standalone CLI command. This prevents flaky GPU/display requirements from blocking ordinary test runs.

## Cross-References

- `src/VoxelForge.Core/Benchmarking/RendererBenchmarkScenes.cs` — Deterministic scene definitions
- `src/VoxelForge.Evaluation/RendererBenchmark.cs` — C# benchmark runner
- `src/VoxelForge.Evaluation/BenchmarkCli.cs` — CLI integration
- `tests/VoxelForge.Evaluation.Tests/RendererBenchmarkTests.cs` — Tests for scenes and benchmark
- `scripts/run-renderer-benchmark.sh` — Wrapper script
- `docs/architecture/electron-renderer-decision-checkpoint.md` — Original Task C scope
- `docs/architecture/bridge-protocol.md#performance-metrics-and-instrumentation` — Bridge metrics notes
