# Voxels + LLMs: Landscape Overview (March 2026)

A comprehensive survey of how voxels and large language models interact across research, open source, and commercial products. Everything listed is from 2023 or later.

## Document Index

| File | Contents |
|------|----------|
| [01-academic-papers.md](01-academic-papers.md) | Research papers on voxel generation, understanding, tokenization, and editing with LLMs |
| [02-3d-tokenization.md](02-3d-tokenization.md) | How 3D/voxel data gets turned into tokens for transformers and LLMs |
| [03-minecraft-agents.md](03-minecraft-agents.md) | LLM agents operating in Minecraft and other voxel worlds |
| [04-open-source-tools.md](04-open-source-tools.md) | GitHub repos, libraries, and open source pipelines |
| [05-mcp-servers.md](05-mcp-servers.md) | MCP servers for 3D/voxel work with AI assistants |
| [06-commercial-products.md](06-commercial-products.md) | Startups, SaaS tools, and commercial offerings |
| [07-voxel-formats-and-libraries.md](07-voxel-formats-and-libraries.md) | File formats, Python/JS libraries, and serialization approaches |
| [08-medical-imaging.md](08-medical-imaging.md) | LLMs applied to voxel-based medical data (CT, MRI) |
| [09-gaps-and-opportunities.md](09-gaps-and-opportunities.md) | Analysis of underserved areas and potential project directions |
| [10-3d-dot-game-heroes-and-frame-animation.md](10-3d-dot-game-heroes-and-frame-animation.md) | 3D Dot Game Heroes technical details, frame-swap animation technique, LLM animation retargeting ideas |

## Key Takeaways

1. **Text-to-voxel is nascent** — most text-to-3D pipelines produce meshes or NeRFs, with voxels as an afterthought or intermediate representation
2. **Minecraft is the dominant voxel+LLM testbed** — Voyager, GITM, Mindcraft, and now multiple MCP servers make it the most active area
3. **3D tokenization is a hot research area** — VoxSet, G3PT, Apple's Shape Tokens, and LATTICE all tackle how to represent 3D data for autoregressive models
4. **MCP servers are exploding** — Blender MCP, MagicaVoxel MCP, and multiple Minecraft MCP servers appeared in 2025
5. **Direct voxel-native LLM tools are rare** — most tools generate meshes then optionally voxelize, rather than operating natively in voxel space
6. **Medical imaging is a parallel track** — VoxelPrompt, M3D-LaMed, and CT-Agent work with volumetric (voxel) data but rarely cross-pollinate with game/creative voxel work
