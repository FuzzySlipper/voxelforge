# VoxelForge MCP Server

`VoxelForge.Mcp` is a headless MCP adapter over the VoxelForge App/Core state and service layer. It does not reference FNA, Myra, or `VoxelForge.Engine.MonoGame`.

## Run

```bash
dotnet run --project src/VoxelForge.Mcp
```

Defaults:

- Listen URL: `http://localhost:5201`
- MCP endpoint: `http://localhost:5201/mcp`
- Project storage directory: `content/`

Overrides:

```bash
dotnet run --project src/VoxelForge.Mcp -- --port 5210 --project-dir /absolute/path/to/projects
# or
dotnet run --project src/VoxelForge.Mcp -- --listen-url http://127.0.0.1:5210
```

Health check:

```bash
curl http://localhost:5201/health
```

## Claude Code configuration

Add an HTTP MCP server entry pointing at the `/mcp` endpoint. Example:

```json
{
  "mcpServers": {
    "voxelforge": {
      "type": "http",
      "url": "http://localhost:5201/mcp"
    }
  }
}
```

Start `VoxelForge.Mcp` first, then launch or reload the MCP client.

## Tools

Core LLM-handler adapters:

- `describe_model` — text summary of the current in-memory voxel document.
- `get_model_info` — JSON model stats, bounds, palette entries, labels, and animation clips.
- `set_voxels` — batch set voxel coordinates to palette indices through undoable App services.
- `remove_voxels` — batch remove voxel coordinates through undoable App services.
- `get_voxels_in_area` — query voxels in a bounding box, including region label id/name when present.
- `view_model`, `view_from_angle`, `compare_reference` — registered visual tool names that return a clear headless-mode error because screenshots require the FNA renderer.

Typed console-command adapters:

- `fill_box` — fill an inclusive cuboid with a palette index.
- `get_voxel` — read one coordinate.
- `count_voxels` — count all voxels, by palette index, or inside an inclusive cuboid.
- `clear_model` — remove all voxels.
- `undo` / `redo` — edit history for the MCP session.
- `console_count` — legacy compatibility adapter over the headless console `count` command using already-tokenized argument arrays; it does not rebuild command-line strings.

Region/label tools:

- `list_regions` — list regions with voxel counts, parent/child/ancestor/descendant ids, properties, and bounds.
- `create_region` — create a named region with optional parent id and string properties.
- `delete_region` — remove a region definition and unlabel its voxels without removing voxels from the model; regions with children must have children deleted first.
- `assign_voxels_to_region` — assign existing model voxels by coordinate list or inclusive box.
- `get_region_voxels` — list coordinates, palette indices, and material names for voxels in a region.
- `get_region_bounds` — return a region axis-aligned bounding box.
- `get_region_tree` — return the full hierarchy as a tree.

Future MCP tools should prefer typed services and request DTOs. Console-command adapters are a compatibility bridge for commands that have not yet been promoted to first-class MCP operations.
