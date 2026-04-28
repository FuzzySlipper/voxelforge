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
- `apply_voxel_primitives` — generate block, box, and line primitive batches through the same validated LLM handler and undoable App service path. Supports `preview_only` for validation/count summaries without mutation.
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
- `publish_preview` — atomically write the current MCP session to a preview `.vforge` file, plus an optional `.preview.json` sidecar manifest, for a GUI started with `--watch` / `--preview-watch`.
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

## Live preview workflow

For a lightweight collaboration preview, keep `VoxelForge.Mcp` as the agent's headless editing session and run the GUI as a watcher over a preview snapshot file. The GUI is an observer of snapshots; the MCP session remains authoritative.

### Default `content/` workflow

```bash
# Terminal 1: MCP server, using the default project storage directory: content/
dotnet run --project src/VoxelForge.Mcp

# Terminal 2: GUI preview window watching the default preview path
dotnet run --project src/VoxelForge.Engine.MonoGame -- --watch content/mcp-preview.vforge
```

Then ask the agent to call `publish_preview`, for example:

```json
{ "name": "mcp-preview" }
```

### Custom project directory workflow

Use the same directory for the MCP server's `--project-dir` and the GUI watch path:

```bash
# Terminal 1: MCP server with isolated live-preview storage
dotnet run --project src/VoxelForge.Mcp -- --project-dir content/live

# Terminal 2: GUI preview window for that storage directory
dotnet run --project src/VoxelForge.Engine.MonoGame -- --watch content/live/mcp-preview.vforge
```

Then publish to the same preview name:

```json
{ "name": "mcp-preview" }
```

For a second concurrent agent/session, use a different project directory or at least a different preview name:

```bash
dotnet run --project src/VoxelForge.Mcp -- --port 5211 --project-dir content/live-agent-b
dotnet run --project src/VoxelForge.Engine.MonoGame -- --watch content/live-agent-b/mcp-preview.vforge
```

### What `publish_preview` writes

`publish_preview`:

- constrains `name` to a file name inside the MCP server's configured project directory;
- defaults to `mcp-preview.vforge` when `name` is omitted;
- writes the `.vforge` snapshot through a same-directory temp file and atomic replace/move;
- writes a sidecar manifest such as `mcp-preview.preview.json` unless `write_manifest` is `false`;
- keeps the manifest's existing absolute `model_path` field for compatibility and also writes portable `model_file` with just the `.vforge` file name;
- returns the resolved snapshot path plus voxel, region, clip, and byte counts.

Example without a sidecar manifest:

```json
{ "name": "mcp-preview", "write_manifest": false }
```

The `.vforge` snapshot metadata currently uses the MCP session's current model name. The MCP session does not yet track project author or original created-at metadata, so preview snapshots use fresh serializer defaults for those fields until session metadata tracking is added.

The GUI watcher accepts either `--watch <path>` or `--preview-watch <path>`. It reloads the `.vforge` on the game update thread after file changes, so a user does not need to manually reopen the file while an agent is working.

### Limitations

- The GUI preview is snapshot-based, not shared-document collaboration. User edits in the GUI are not synchronized back into the MCP session.
- `VoxelForge.Mcp` is still headless and does not depend on FNA/Myra/Engine.
- MCP visual tools such as `view_model`, `view_from_angle`, and `compare_reference` still return headless limitation errors. The preview window is for a human observer, not screenshot capture through MCP.
- If an MCP process exits before `publish_preview` or `save_model`, unsaved in-memory session edits are lost.

### Troubleshooting

- **Preview window stays empty:** confirm the watched path matches the MCP project directory and preview name. With defaults, watch `content/mcp-preview.vforge` and publish `{ "name": "mcp-preview" }`.
- **File not found at startup:** this is expected if the agent has not published yet. The watcher creates/listens to the directory and loads after the first snapshot appears.
- **Stale preview:** ask the agent to call `publish_preview` again, then check the sidecar manifest's `updated_at_utc` and `voxel_count`.
- **Multiple agents overwrite each other:** give each agent a separate `--project-dir`, `--port`, or preview `name`.
- **Headless run ignores watch mode:** `--watch` requires the GUI renderer. `dotnet run --project src/VoxelForge.Engine.MonoGame -- --headless --watch ...` prints a warning and does not start the preview watcher.

Future preview push/launcher options are discussed in [`architecture/mcp-live-preview-follow-up-options.md`](architecture/mcp-live-preview-follow-up-options.md). MCP tools should prefer typed services and request DTOs. Console-command adapters are a compatibility bridge for commands that have not yet been promoted to first-class MCP operations.
