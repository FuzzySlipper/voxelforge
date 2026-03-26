# Minecraft & Voxel Game LLM Agents (2023–2026)

Minecraft is by far the most active intersection of LLMs and voxel worlds. The game's voxel-based environment provides a natural testbed for embodied AI agents.

## Major Research Projects

### Voyager: An Open-Ended Embodied Agent with Large Language Models
- **Date**: May 2023 (TMLR/ICLR 2025 Journal Track)
- **Link**: https://voyager.minedojo.org/ | [arXiv](https://arxiv.org/abs/2305.16291) | [GitHub](https://github.com/MineDojo/Voyager)
- **What it does**: First LLM-powered embodied lifelong learning agent in Minecraft. Continuously explores, acquires skills, and makes discoveries without human intervention.
- **Architecture**: Three components — automatic curriculum for exploration, ever-growing skill library of executable code, iterative prompting with environment feedback and self-verification.
- **LLM**: GPT-4 via blackbox queries (no fine-tuning needed)
- **Performance**: 3.1x more unique items than prior SOTA, tech tree milestones 15.3x faster, 2.3x longer travel distances. Skills transfer to new worlds.

### Ghost in the Minecraft (GITM)
- **Date**: May 2023 (NeurIPS 2024)
- **Link**: https://github.com/OpenGVLab/GITM | [arXiv](https://arxiv.org/abs/2305.17144)
- **What it does**: Hierarchical LLM-based agent that unlocks 100% of Minecraft Overworld tech tree items (previous agents combined: 30%).
- **Architecture**: LLM Decomposer → LLM Planner → LLM Interface (goals → sub-goals → structured actions → keyboard/mouse ops)
- **Performance**: 67.5% success on ObtainDiamond (+47.5% over OpenAI VPT). No GPU training needed — runs on CPU in 2 days vs. VPT's 6,480 GPU-days.

### Mindcraft: Minecraft AI with LLMs + Mineflayer
- **Date**: 2024-2025 (actively maintained)
- **Link**: https://github.com/kolbytn/mindcraft | [Community Edition](https://github.com/mindcraft-ce/mindcraft-ce)
- **What it does**: Extensible platform for LLM agents controlling Minecraft characters. Primary bot "Andy" communicates with players and autonomously sets goals (gathering, building, etc.).
- **LLM Support**: OpenAI, Anthropic (Claude), Gemini, Replicate, Hugging Face, Groq, Ollama
- **Research**: Published "Collaborating Action by Action: A Multi-agent LLM Framework for Embodied Reasoning" (2025). Finding: communication is the primary bottleneck — performance drops ~15% when agents must communicate detailed plans.
- **Community**: Active community edition (mindcraft-ce) with ongoing development

### Co-Voyager
- **Link**: https://github.com/Itakello/Co-voyager
- **What it does**: Multi-agent extension of Voyager for collaborative Minecraft gameplay.

### MineDojo
- **Link**: https://minedojo.org/
- **What it does**: Research framework providing a massive suite of Minecraft tasks, internet-scale knowledge base (YouTube videos, wiki pages, Reddit posts), and simulator API. Foundation for Voyager and other agents.

### STEVE-1
- **What it does**: Instruction-following Minecraft agent trained on gameplay videos. Uses text/visual instructions to perform tasks.

### Talking-to-Build: LLM-Assisted Interface for Minecraft
- **Date**: 2025
- **Link**: https://dl.acm.org/doi/10.1145/3716553.3756015
- **Summary**: Studies how LLM-assisted natural language interfaces affect player performance and experience when building in Minecraft.

## MCP Servers for Minecraft (2025)

These allow any MCP-compatible AI (Claude, etc.) to control Minecraft bots:

### MoLing-Minecraft
- **Link**: https://github.com/gojue/moling-minecraft
- **What it does**: AI Agent MCP server for intelligent construction, building, and game control. Supports complex builds and redstone circuits via natural language.

### Minecraft MCP Server (Yuniko)
- **Link**: https://github.com/yuniko-software/minecraft-mcp-server
- **What it does**: Mineflayer-powered MCP server for real-time character control. Build structures, explore, interact via natural language.

### MCPMC
- **Link**: https://mcpmarket.com/server/mcpmc
- **What it does**: MCP server with standardized JSON-RPC interface for bot control — navigation, block manipulation, inventory management, real-time state monitoring.

### Minecraft Bot Control (leo4life2)
- **What it does**: Open-source bridge exposing in-game actions (move, dig, build) as standardized MCP tools.

### Mc-Agent (Bedrock Edition)
- **Link**: https://mcpmarket.com/server/mc-agent
- **What it does**: Integrated system for Minecraft Bedrock Edition with AIAgent MCP server + WebSocket/Script API MC server.

## Additional Research Projects

### JARVIS-1: Open-World Multi-Task Agent
- **Date**: 2023
- **Link**: https://arxiv.org/abs/2311.05997
- **What it does**: Combines LLM planning with pre-trained visual models and multimodal memory. Strong performance on MineDojo benchmark.

### DEPS: Describe, Explain, Plan, and Select
- **Date**: 2023
- **Link**: https://arxiv.org/abs/2302.01560
- **What it does**: LLM-based planning with GPT-4 for task decomposition and error recovery in Minecraft.

### Plan4MC
- **Date**: 2023
- **Link**: https://arxiv.org/abs/2303.16563
- **What it does**: LLM skill planning — decomposes complex tasks into basic skill sequences.

### ODYSSEY
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2407.15325
- **What it does**: Comprehensive benchmark evaluating LLM agents across exploration, combat, and construction.

### ROCKET-1
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2410.17856
- **What it does**: Multimodal agent combining VLMs with Minecraft gameplay, uses visual grounding.

### MineAgent
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2410.07407
- **What it does**: Multi-modal agent framework leveraging VLMs for perception and planning.

### MP5: Multi-Modal Planning for Minecraft
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2312.07472
- **What it does**: Incorporates vision, language, and action modalities for Minecraft planning.

### IGLU: Interactive Grounded Language Understanding
- **Date**: NeurIPS 2022-2024
- **Link**: https://arxiv.org/abs/2205.01714 | [GitHub](https://github.com/iglu-contest)
- **What it does**: Benchmark for collaborative building in voxel worlds. Architect gives NL instructions, builder agent constructs. Multiple teams applied LLMs.

### Oasis (Decart) — Diffusion-Generated Minecraft World
- **Date**: 2024
- **Link**: https://github.com/etched-ai/oasis
- **What it does**: Real-time playable Minecraft-like world generated entirely by a diffusion video model. Not LLM-driven but demonstrates AI-generated voxel worlds.

### Foundation Models
- **VPT** (OpenAI, 2022): Trained on 70K+ hours of Minecraft gameplay. Foundation for STEVE-1 and others. [GitHub](https://github.com/openai/Video-Pre-Training)
- **GROOT** (ICLR 2024): Foundation world model learning from unlabeled video, zero-shot task transfer. [arXiv](https://arxiv.org/abs/2310.08235)
- **MineCLIP**: CLIP-style model trained on Minecraft YouTube videos + transcripts. Provides reward signal for agents.

### Other Voxel Games
- **Roblox AI Tools** (2024-2025): LLM-powered building tools and code generation (Roblox Assistant) integrated into the platform.

## Key Insight for Tool Building

Minecraft MCP servers are the most mature example of "LLM controls voxel world." The pattern is:
1. LLM receives game state as text/structured data
2. LLM generates actions (place block, mine, move)
3. Mineflayer (or similar) executes actions in-game
4. Results fed back to LLM

This loop could be adapted for any voxel editor/engine, not just Minecraft.
