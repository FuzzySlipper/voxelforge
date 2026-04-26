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

## Initial tools

- `describe_model` — read-only LLM-tool adapter that describes the current in-memory voxel document.
- `console_count` — compatibility adapter over the headless console `count` command using already-tokenized argument arrays; it does not rebuild command-line strings.

Future MCP tools should prefer typed services and request DTOs. Console-command adapters are a compatibility bridge for commands that have not yet been promoted to first-class MCP operations.
