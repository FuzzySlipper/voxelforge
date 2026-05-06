# VoxelForge

LLM-assisted voxel authoring tool. Frame-swap animation, semantic region labels, built-in LLM assistance.

Built with C#/.NET 10, FNA (XNA reimplementation), and Myra UI.

## Prerequisites

- .NET 10 SDK
- CMake, Make, GCC
- SDL3 (`pacman -S sdl3` on Arch)
- Vulkan-capable GPU and drivers
- Node.js 20+ and npm (for Electron smoke test only)

## Building

```bash
# Initialize submodules (first time only)
git submodule update --init --recursive

# Build native libraries (FNA3D + FAudio against system SDL3)
./build-native.sh

# Build the .NET solution
dotnet build voxelforge.slnx

# Run
dotnet run --project src/VoxelForge.Engine.MonoGame
```

After the initial setup, the day-to-day cycle is just `dotnet build` + `dotnet run`. Re-run `./build-native.sh` only if you update the FNA submodule or clean the build output.

## Running Tests

```bash
dotnet test voxelforge.slnx
```

## Bridge Smoke Tests

The Electron renderer experiment uses a C# sidecar (`VoxelForge.Bridge`) that hosts a `den-bridge` WebSocket server. Three smoke-test scripts verify the bridge end-to-end:

```bash
# C#-only smoke test (ping + version handshake via WebSocket, no Electron needed)
./scripts/run-bridge-smoke-test.sh

# Full Electron + C# sidecar smoke test (spawns sidecar, connects, pings, exits)
./scripts/run-electron-smoke-test.sh

# Headless renderer smoke test (spawns sidecar + Electron, captures renderer metrics, exits)
./scripts/run-renderer-smoke-test.sh
```

The Electron smoke tests require `cd electron && npm install` on first run (the scripts handle this automatically).

See [`docs/architecture/bridge-protocol.md`](docs/architecture/bridge-protocol.md) for the full VoxelForge bridge protocol specification.

## Electron Renderer (Experiment)

The [`electron/`](electron/) directory contains an experimental Electron + Three.js renderer that communicates with a C# sidecar over the `den-bridge` WebSocket protocol.

### Prerequisites

- All [prerequisites](#prerequisites) above (for the C# sidecar)
- Node.js 20+ and npm
- Git submodules initialized (`git submodule update --init --recursive` also pulls `lib/den-bridge`)

### Development Workflow

The recommended dev loop uses `scripts/run-electron-dev.sh`, which handles the full build chain:

```bash
# Build C# sidecar + TypeScript + launch Electron (sidecar auto-spawned)
./scripts/run-electron-dev.sh

# Launch with a preview .vforge file auto-loaded
./scripts/run-electron-dev.sh --preview content/mcp-preview.vforge

# Start the C# sidecar standalone (for inspecting bridge protocol)
./scripts/run-electron-dev.sh --sidecar-only
```

Or run the individual steps manually:

```bash
# Install JS dependencies (first time only)
cd electron && npm install

# Build TypeScript sources
cd electron && npm run build

# Build + launch Electron (build happens automatically)
cd electron && npm start

# Launch Electron with a preview file
cd electron && npm start -- --preview ../content/mcp-preview.vforge

# Start the C# sidecar independently (for debugging)
dotnet run --project src/VoxelForge.Bridge
```

The Electron main process discovers the repo root automatically (via `voxelforge.slnx`) and spawns the C# sidecar as a child process. When the Electron window closes, the sidecar is terminated cleanly.

### Packaging Workflow

The C# sidecar (`VoxelForge.Bridge`) is published as a self-contained .NET binary and bundled into Electron packages via electron-builder's `extraResources`.

#### Single-command package builds

```bash
# Directory-only build (unpackaged directory for testing)
cd electron && npm run package:dir

# Full AppImage build (requires AppImage tooling)
cd electron && npm run package
```

Both commands run the `publish-sidecar` step automatically: `dotnet publish` produces a self-contained binary under `electron/sidecar/`, which `electron-builder` copies into the package's `resources/sidecar/` directory.

The Electron main process discovers the sidecar automatically:

- **From the repository root** (`voxelforge.slnx` found by walking up from `__dirname`): spawns `dotnet run --project src/VoxelForge.Bridge` (unchanged dev mode).
- **From a packaged directory** (no `voxelforge.slnx` found): spawns `process.resourcesPath/sidecar/VoxelForge.Bridge` directly.

#### Standalone publish (without packaging)

```bash
./scripts/publish-sidecar.sh
```

Publishes the sidecar to `electron/sidecar/` as a self-contained binary for `linux-x64`.

### Validation Before Review

Run these commands to validate changes before requesting review:

```bash
# 1. C# build and all tests
dotnet build voxelforge.slnx
dotnet test voxelforge.slnx

# 2. C# bridge smoke test
dotnet build src/VoxelForge.Bridge && ./scripts/run-bridge-smoke-test.sh

# 3. TypeScript compilation
cd electron && npm run build

# 4. TypeScript unit tests (headless, no GPU required)
cd electron && npm test

# 5. Electron packaging smoke test (directory build)
cd electron && npm run package:dir

# 6. (Optional) Full Electron smoke test with running sidecar
./scripts/run-electron-smoke-test.sh
./scripts/run-renderer-smoke-test.sh
```

### Experiment Decision Checkpoint

The Electron renderer experiment has been evaluated. See [`docs/architecture/electron-renderer-decision-checkpoint.md`](docs/architecture/electron-renderer-decision-checkpoint.md) for the full decision record.

**Current posture:** Keep the Electron renderer as a **parallel experimental renderer**. The core architecture (bridge protocol, sidecar, incremental mesh pipeline) is proven, but packaging, performance profiling, and feature parity are incomplete. The existing FNA/Myra frontend remains the supported control path.

### TypeScript Tests

The `electron/` project includes a headless TypeScript test suite powered by [Vitest](https://vitest.dev). Tests cover pure helper functions extracted from the renderer, bridge client, and scene code — no GPU, WebGL, or Electron display is required.

```bash
# Run all TS tests (headless)
cd electron && npm test

# Watch mode for development
cd electron && npm run test:watch
```

Test structure:

- `tests/byte-utils.test.ts` — Byte array decoding from base64, number[], and Uint8Array sources
- `tests/compute-placement.test.ts` — Voxel placement position calculation from raycast hits
- `tests/string-utils.test.ts` — String utilities (titleCase, formatError, escapeHtml)
- `tests/frame-parser.test.ts` — Bridge protocol frame parsing (response/event dispatch)
- `tests/mesh-construction.test.ts` — Mesh snapshot data integrity, vertex color processing, buffer layout

Test fixture data lives in `tests/fixtures/` and includes known mesh snapshot data (cube voxels, empty models, base64-encoded fixtures).

### Known Limitations

- **Linux only:** Packaging targets are currently Linux-only (`AppImage` + `dir`). Windows/macOS packaging is not configured (see `electron/electron-builder.yml` for target configuration).
- **Self-contained sidecar size:** The `dotnet publish --self-contained` sidecar is approximately 60–120 MB due to the bundled .NET runtime. This could be reduced with `PublishTrimmed` / `PublishReadyToRun` in future work.
- **AppImage validation:** `npm run package` produces an AppImage containing the bundled sidecar, but AppImage execution requires FUSE or `--appimage-extract` in headless/CI environments. Directory builds (`package:dir`) are the recommended validation path.
- **Bootstrap context:** The `electron/` directory is a separate npm/TypeScript project. The `./scripts/build-electron.sh` script handles the combined C# + Electron build for CI-like validation.

## Project Structure

```
src/
  VoxelForge.Core              Pure data model, operations, meshing, serialization, LLM abstractions
  VoxelForge.Content           Palette definitions, templates, default assets
  VoxelForge.LLM               LLM provider adapters (Microsoft.Extensions.AI)
  VoxelForge.App               Editor state, undo/redo, tools, composition
  VoxelForge.Engine.MonoGame   FNA rendering, Myra UI panels, input handling
  VoxelForge.Bridge            Headless bridge sidecar for Electron renderer experiment

tests/
  VoxelForge.Core.Tests        Data model, labels, animation, serialization, meshing, raycasting
  VoxelForge.LLM.Tests         Tool loop, handler dispatch
  Architecture.Tests           Dependency boundary enforcement
  VoxelForge.Bridge.Tests      Bridge handler and WebSocket transport smoke tests

lib/                           Git submodules (FNA, Myra, FontStashSharp, XNAssets, den-bridge)

electron/                      Electron renderer experiment (TypeScript, minimal smoke test)
```

## Controls

- **Right-drag** — Orbit camera
- **Scroll wheel** — Zoom
- **F1/F2/F3** — Snap to front/side/top view
- **F4** — Toggle wireframe
- **1-6** — Select tool (Place, Remove, Paint, Select, Fill, Label)
- **Ctrl+Z / Ctrl+Y** — Undo / Redo
- **Delete** — Remove selected voxels
- **Escape** — Exit

## License

MIT
