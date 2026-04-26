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

Model lifecycle and palette tools:

- `new_model` — replace the active session document with a new empty model, grid hint, and optional palette entries.
- `load_model` / `save_model` — load and save `.vforge` files by name under the configured project directory.
- `list_models` — list available `.vforge` files in the configured project directory.
- `list_palette` — list current palette entries with RGBA colors.
- `set_palette_entry` — add or update a palette entry through undoable App services.
- `set_grid_hint` — set the model advisory grid resolution through undoable App services.

Spatial reasoning tools:

- `get_region_neighbors` — find labeled regions adjacent to a region using configurable 6- or 26-connected voxel adjacency.
- `get_interface_voxels` — return boundary voxel pairs and unique boundary voxels for two regions.
- `measure_distance` — measure point-to-point distance or region centroid / nearest-surface distance.
- `get_cross_section` — return a compact 2D text slice along `x`, `y`, or `z`, with a legend for region or palette symbols.
- `check_collision` — test whether two regions, boxes, or a region and box overlap in occupied voxel coordinates.

Future MCP tools should prefer typed services and request DTOs. Console-command adapters are a compatibility bridge for commands that have not yet been promoted to first-class MCP operations.
