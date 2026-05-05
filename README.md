# VoxelForge

LLM-assisted voxel authoring tool. Frame-swap animation, semantic region labels, built-in LLM assistance.

Built with C#/.NET 10, FNA (XNA reimplementation), and Myra UI.

## Prerequisites

- .NET 10 SDK
- CMake, Make, GCC
- SDL3 (`pacman -S sdl3` on Arch)
- Vulkan-capable GPU and drivers

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

## Project Structure

```
src/
  VoxelForge.Core              Pure data model, operations, meshing, serialization, LLM abstractions
  VoxelForge.Content           Palette definitions, templates, default assets
  VoxelForge.LLM               LLM provider adapters (Microsoft.Extensions.AI)
  VoxelForge.App               Editor state, undo/redo, tools, composition
  VoxelForge.Engine.MonoGame   FNA rendering, Myra UI panels, input handling

tests/
  VoxelForge.Core.Tests        Data model, labels, animation, serialization, meshing, raycasting
  VoxelForge.LLM.Tests         Tool loop, handler dispatch
  Architecture.Tests           Dependency boundary enforcement

lib/                           Git submodules (FNA, Myra, FontStashSharp, XNAssets, den-bridge)
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
