# MCP Servers for 3D & Voxel Work (2025–2026)

MCP (Model Context Protocol) servers allow AI assistants like Claude to directly control 3D tools. This is one of the fastest-growing areas.

## Voxel-Specific MCP Servers

### MagicaVoxel MCP
- **Link**: https://github.com/Mahinika/magicavoxel-mcp | [LobeHub](https://lobehub.com/mcp/mahinika-magicavoxel-mcp)
- **What it does**: MCP server for reading/writing MagicaVoxel .vox files programmatically. Create, modify, and manage voxel models through natural language prompts.
- **Setup**: Configure VOX_DIR and EXPORT_DIR env vars. Works with Cursor, Claude, etc.
- **Limitations**: Basic .vox read/write. Complex features (materials, animations) require manual MagicaVoxel editing.

### Minecraft MCP Servers
See [03-minecraft-agents.md](03-minecraft-agents.md) for 5+ Minecraft MCP servers (MoLing, Yuniko, MCPMC, leo4life2, Mc-Agent).

## 3D Modeling MCP Servers

### BlenderMCP (ahujasid — original)
- **Link**: https://github.com/ahujasid/blender-mcp | [Website](https://blender-mcp.com/)
- **What it does**: Connects Blender to Claude AI via MCP. Two-way socket-based communication for object manipulation, material control, scene inspection.
- **Voxel feature**: Includes `voxel_remesh` tool for rebuilding meshes with uniform voxel-based topology.
- **Coverage**: [Hackaday writeup](https://hackaday.com/2025/05/18/mcp-blender-addon-lets-ai-take-the-wheel-and-wield-the-tools/)

### Blender MCP Server (PolyMCP — 51 tools)
- **Link**: https://github.com/poly-mcp/Blender-MCP-Server
- **What it does**: More comprehensive Blender MCP with 51 tools. Thread-safe execution, auto-dependency installation, complete 3D workflow automation.

### OpenSCAD MCP Server
- **Link**: Referenced in [skywork.ai analysis](https://skywork.ai/skypage/en/ai-engineer-openscad-mcp-server/1980872653259997184)
- **What it does**: Allows AI assistants to generate and execute OpenSCAD code for parametric 3D modeling.

### Revit MCP
- **What it does**: Connects AI to Autodesk Revit (BIM software) for architectural/engineering model control via natural language.

### AutoCAD LT MCP Server
- **What it does**: Translates natural language into AutoLISP instructions for AutoCAD LT 2024+.

### SketchUp MCP
- **What it does**: Natural language control of SketchUp. Notable for woodworking-specific tools (mortise/tenon, dovetail, finger joints).

### Rhino MCP Server
- **What it does**: Natural language control of Rhino 3D's NURBS modeling.

### Meshy AI MCP Server
- **Link**: https://mcp.aibase.com/server/1916354958470389762
- **What it does**: Interaction with Meshy AI API for 3D model generation.

## The MCP Pattern for Voxels

The typical MCP architecture for 3D/voxel work:

```
User (natural language) → AI Assistant (Claude/GPT) → MCP Server → 3D Application
                                                                    ↓
                                                              State/feedback
                                                                    ↓
                                                         AI Assistant (next action)
```

**Key resources**:
- [Snyk: 6 MCP Servers for 3D Models](https://snyk.io/articles/6-mcp-servers-for-using-ai-to-generate-3d-models/)
- [Snyk: 9 MCP Servers for CAD](https://snyk.io/articles/9-mcp-servers-for-computer-aided-drafting-cad-with-ai/)
- [From Blender-MCP to 3D-Agent](https://dev.to/glglgl/from-blender-mcp-to-3d-agent-the-evolution-of-ai-powered-blender-modeling-1m7d)

## Gap Analysis

What's **missing** in the MCP ecosystem for voxels:
- No MCP server for **direct voxel grid manipulation** (create/read/write voxel grids natively without going through a full 3D app)
- No MCP server for **voxelizing arbitrary meshes** (take a .obj → produce voxels)
- No MCP server for **VDB/OpenVDB** operations
- No MCP server for **Minecraft schematic** (.schem) editing outside of a running game
- Limited support for **voxel-native operations** (flood fill, boolean operations on voxel grids, etc.)
