# AGENTS.md (Project Local)

## Mission

An LLM-assisted voxel authoring tool. Frame-swap animation, semantic region labels, built-in LLM assistance via a debug console and stdio protocol. The core thesis: voxels are uniquely LLM-friendly among 3D representations — coordinates and values are meaningful without a decode step, enabling generate-and-refine workflows.

Rendering: **FNA** (XNA4 reimplementation) with **FNA3D** (Vulkan via SDL_GPU on Linux). UI: **Myra**. Both are git submodules, not NuGet packages.

## Task Management

Tasks are tracked via the Den MCP server (project ID: `voxelforge`).

**Available MCP tools:**

- `mcp__den__list_tasks` — list all tasks and status
- `mcp__den__get_task` — get full task details including dependencies and subtasks
- `mcp__den__next_task` — find the next unblocked task to work on
- `mcp__den__create_task` — create a new task or subtask
- `mcp__den__update_task` — update task fields and status (requires agent identity)
- `mcp__den__add_dependency` / `mcp__den__remove_dependency` — manage task dependencies
- `mcp__den__send_message` — send a message on a task or thread
- `mcp__den__store_document` / `mcp__den__search_documents` — store and search project documents

**Current milestones:** Foundation (done), Reference Models & Vision. See `docs/` for PRDs.

## Hard Architectural Rules

These are non-negotiable. Do not suggest alternatives.

- **No static singletons.** No `ServiceLocator`, no `Instance` patterns. All dependencies through constructors.
- **No reflection for registration.** All handler, command, and service registration is explicit (see `CommandRegistry.cs`, `ToolRegistry.cs`).
- **No engine types in VoxelForge.Core.** Core has zero references to FNA, Myra, or any rendering library. Use plain structs and interfaces.
- **Dependency direction is fixed:** `Engine.MonoGame → App → {LLM, Content} → Core`. No cycles. Architecture tests enforce this.
- **DisableTransitiveProjectReferences** is enabled. If a project needs a type, it must explicitly reference the project that defines it.
- **No lambdas/closures for tool handlers or commands.** All handlers are named types implementing interfaces (`IToolHandler`, `IConsoleCommand`, `IEditorTool`).
- **Nullable reference types enabled, warnings as errors.** All public API must have nullable annotations.
- **No mocking libraries.** Use hand-written test fakes (see `FakeCompletionService.cs`).
- **Avoid LINQ in hot paths.** Prefer clear, verbose loops. LINQ is acceptable in non-performance-critical code (commands, serialization).

## Core Data Model

### VoxelModel (source of truth)

Sparse `Dictionary<Point3, byte>` — byte is a palette index, absence means air. Not an array, not an octree. The authoring model is deliberately simple; rendering structures are derived.

```csharp
readonly record struct Point3(int X, int Y, int Z);
model.SetVoxel(new Point3(5, 3, 2), paletteIndex);
byte? value = model.GetVoxel(pos);  // null = air
```

`GridHint` is advisory (suggested resolution), not enforced. Voxels can exist at any coordinate.

### Palette

`Palette` maps byte indices (1-255) to `MaterialDef` (name, RGBA color, metadata). Index 0 is reserved/air.

### Semantic Labels

Labels are region-based, not per-voxel. `RegionDef` stores a `HashSet<Point3>` of voxels belonging to that region, plus optional `ParentId` for hierarchy (body → right_arm → right_hand). `LabelIndex` is a **derived** dual-indexed lookup rebuilt at runtime — never serialized. A voxel belongs to at most one region.

### Animation

Frame-swap: `AnimationClip` has a shared `Base` VoxelModel + `List<AnimationFrame>` of override dictionaries. `ResolveFrame(i)` returns base + overrides merged into a complete VoxelModel. Overrides are `Dictionary<Point3, byte?>` — null means "remove this voxel in this frame."

### Meshing

`IVoxelMesher` strategy interface. `GreedyMesher` (primary) and `NaiveMesher` (debug baseline). Both produce `VoxelMesh` (vertex/index arrays) from a VoxelModel. Interior faces are culled. FNA/XNA defaults to `RasterizerState.CullCounterClockwise`, so mesh winding and rasterizer state must stay aligned; verify winding changes with code-side tests against the active render-path convention rather than relying on visual inspection alone.

## Command & Undo System

All model mutations go through `IEditorCommand` → `UndoStack`. Commands capture old state in the constructor, apply in `Execute()`, restore in `Undo()`. `CompoundCommand` groups multiple commands for single undo (e.g., LLM-generated batches, fill operations).

The `UndoStack` fires `StateChanged` events so the renderer marks itself dirty.

## Console & Stdio Protocol

Three execution modes, all sharing the same `CommandRouter` → `IConsoleCommand` infrastructure:

1. **Interactive console** — Spectre.Console + ReadLine (tab completion, history) in the terminal alongside the FNA window.
2. **Stdio JSON-line** — `{"command":"set","args":["5","5","5","1"]}` → `{"ok":true,"message":"Set (5,5,5) = 1"}`. Activated when stdin is piped.
3. **Headless** — `--headless` flag, no FNA window. Stdio or interactive.

All commands: `help`, `describe`, `set`, `remove`, `fill`, `get`, `getcube`, `getsphere`, `count`, `palette`, `regions`, `label`, `undo`, `redo`, `save`, `load`, `list`, `clear`, `grid`, `config`.

Save/load defaults to `content/` directory with `.vforge` extension.

## LLM Integration

`ICompletionService` (Core) abstracts LLM providers. `ToolLoop` (Core) is the single call-dispatch-repeat engine. `IToolHandler` implementations are named classes in `Core.LLM.Handlers/`. `ChatClientCompletionService` (VoxelForge.LLM) adapts `Microsoft.Extensions.AI.IChatClient`.

Tool handlers return `ToolHandlerResult` with a string content (for the LLM) and an optional `ApplyAction` (deferred model mutation). The stdio protocol enables external agents (Claude Code) to puppet the app directly.

## FNA Ecosystem (Submodules)

FNA replaces MonoGame. Same `Microsoft.Xna.Framework` namespace — code is API-compatible.

**Key differences from MonoGame:**
- `DrawIndexedPrimitives` takes 6 parameters (XNA4 signature), not MonoGame's 3.
- FNA uses system SDL3, not a bundled SDL2. No `sdl2-compat` issues.
- FNA3D auto-selects Vulkan on Linux via SDL_GPU.
- `TextButton` doesn't exist in Myra with FNA — use `Button` with `Content = new Label { Text = "..." }`.

**Submodule layout:**
```
lib/
  FNA/                     FNA framework (+ nested: SDL3-CS, FAudio, FNA3D, Theorafile, dav1dfile)
  Myra/                    UI framework
  FontStashSharp/           Font rendering (Myra dependency)
  XNAssets/                 Asset loading (Myra dependency)
```

`lib/Directory.Build.props` and `lib/Directory.Packages.props` isolate submodules from VoxelForge's strict build settings. Submodule `Directory.Build.props` files are patched to target net10.0 (local modifications, not committed upstream).

**Native libraries:** `libFNA3D.so` and `libFAudio.so` are built from source via `./build-native.sh`. Requires cmake, gcc, SDL3 dev headers.

## Configuration

`config.json` in working directory. Managed via `config` console command. Keys: `invertOrbitX`, `invertOrbitY`, `orbitSensitivity`, `zoomSensitivity`, `defaultGridHint`, `maxUndoDepth`, `backgroundColor`.

## Before Editing

1. Check the current task via `mcp__den__next_task` or `mcp__den__get_task` for scope and acceptance criteria.
2. Read the relevant section of this file for the subsystem you're changing.
3. Build: `dotnet build voxelforge.slnx`

## While Editing

1. Do not add FNA/Myra types to Core, Content, LLM, or App.
2. All model mutations go through the undo system (`IEditorCommand` → `UndoStack`).
3. New console commands must work in both interactive and stdio modes.
4. New tool handlers are named classes implementing `IToolHandler` — register in `CommandRegistry.cs`.

## After Editing

1. `dotnet build voxelforge.slnx` — must pass with zero warnings.
2. `dotnet test voxelforge.slnx` — must pass all tests.
3. Architecture tests must pass (dependency boundary enforcement).

## Project Structure

```
src/VoxelForge.Core              — data model, operations, meshing, serialization, LLM abstractions
src/VoxelForge.Content           — palette definitions, reference model loading
src/VoxelForge.LLM               — LLM provider adapters (Microsoft.Extensions.AI)
src/VoxelForge.App               — editor state, undo/redo, tools, console, commands, config
src/VoxelForge.Engine.MonoGame   — FNA rendering, Myra UI panels, input handling, screenshots

tests/VoxelForge.Core.Tests      — data model, labels, animation, serialization, meshing, raycasting, undo
tests/VoxelForge.LLM.Tests       — tool loop, handler dispatch
tests/Architecture.Tests         — dependency boundary enforcement

lib/                             — git submodules (FNA, Myra, FontStashSharp, XNAssets)
content/                         — saved .vforge project files
docs/research/                   — landscape research documents
```

## Build & Test

```bash
git submodule update --init --recursive   # first time only
./build-native.sh                          # build FNA3D + FAudio native libs
dotnet build voxelforge.slnx
dotnet test voxelforge.slnx
dotnet run --project src/VoxelForge.Engine.MonoGame              # GUI + console
dotnet run --project src/VoxelForge.Engine.MonoGame -- --headless # headless mode
```

All VoxelForge projects target `net10.0`. Submodules also patched to `net10.0`.
