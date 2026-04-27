# VoxelForge Local Bootstrap

Project-specific live guidance lives in Den at `[doc: voxelforge/project-bootstrap-guide]`.

Use project ID `voxelforge` for Den tasks, messages, documents, librarian queries, and guidance lookups.

## Local Commands

```bash
git submodule update --init --recursive
./build-native.sh
dotnet build voxelforge.slnx
dotnet test voxelforge.slnx
dotnet run --project src/VoxelForge.Engine.MonoGame
dotnet run --project src/VoxelForge.Engine.MonoGame -- --headless
```
