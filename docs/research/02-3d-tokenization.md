# 3D Tokenization for LLMs and Transformers (2023–2026)

A critical question for voxel+LLM work: how do you turn 3D spatial data into tokens that transformers can process?

## Core Approaches

### VoxSet: Sparse Voxel Set Tokenizer for 3D Shape Generation
- **Date**: 2025 (ICLR 2025)
- **Link**: https://openreview.net/forum?id=7cLvFw1ZGu
- **Key Idea**: Combines sparse voxels in outer layers (for surface detail) with a vector set bottleneck (for compression). Produces **fixed-length** latent codes regardless of object complexity — critical for autoregressive generation.
- **Why it matters**: Previous sparse voxel tokenizers produce variable-length tokens requiring two-stage pipelines. VoxSet eliminates this.

### G3PT: Unleash the Power of Autoregressive Modeling in 3D Generation
- **Date**: September 2024 (IJCAI 2025)
- **Link**: https://arxiv.org/abs/2409.06322 | [GitHub](https://github.com/Zeqiang-Lai/LATTICE)
- **Key Idea**: Coarse-to-fine autoregressive 3D generation. Maps point-based 3D data into discrete tokens at different detail levels. Uses Cross-Scale Querying Transformer (CQT) with cross-attention across scales. Uses Lookup-Free Quantization (LFQ).
- **Why it matters**: First to show clear scaling-law behavior in 3D generation (more compute = better quality).

### Apple: 3D Shape Tokenization via Latent Flow Matching
- **Date**: December 2024
- **Link**: https://arxiv.org/abs/2412.15618 | [Apple ML](https://machinelearning.apple.com/research/3d-shape-tokenization)
- **Key Idea**: "Shape Tokens" — continuous, compact vectors representing 3D surfaces as probability density functions. Uses flow-matching in 3D space. Only requires point clouds as input (minimal preprocessing).
- **Capabilities**: Shape generation, image-to-3D, text-3D alignment, zero-shot surface normal estimation. Variable resolution rendering at inference.

### LATTICE: Democratize High-Fidelity 3D Generation at Scale
- **Date**: November 2025 (CVPR 2026)
- **Link**: https://arxiv.org/abs/2512.03052 | [GitHub](https://github.com/Zeqiang-Lai/LATTICE)
- **Key Idea**: Uses VoxSet representation — compresses 3D assets into latent vectors anchored to a **coarse voxel grid**. Two-stage pipeline: (1) generate sparse voxel geometry anchor, (2) produce detailed geometry via rectified flow transformer. Supports arbitrary resolution decoding.
- **Why it matters**: State-of-the-art 3D generation quality with explicit voxel-based structure.

### ShapeGPT: 3D Shape Generation with a Unified Multi-modal Language Model
- **Date**: November 2023
- **Link**: https://arxiv.org/html/2311.17618v1
- **Key Idea**: Uses a shape-specific VQ-VAE to convert 3D shapes into 512 tokens (flattened 8x8x8 grid). Feeds these into T5 language model for sequence-to-sequence multimodal tasks.
- **Why it matters**: Early demonstration of treating 3D shapes as "language" tokens processable by standard LLMs.

### 3D Representation in 512-Byte: Variational Tokenizer
- **Date**: December 2024
- **Link**: https://arxiv.org/html/2412.02202v1
- **Summary**: Extreme compression of 3D representations into 512 bytes, enabling autoregressive 3D generation with very compact tokens.

### VAR-3D: View-aware Auto-Regressive Model for Text-to-3D via 3D Tokenizer
- **Date**: 2025
- **Link**: https://arxiv.org/html/2602.13818
- **Summary**: View-aware autoregressive approach using a dedicated 3D tokenizer for text-to-3D generation.

### MeshGPT: Generating Triangle Meshes with Decoder-Only Transformers
- **Date**: 2024 (CVPR 2024)
- **Link**: https://nihalsid.github.io/mesh-gpt/static/MeshGPT.pdf
- **Summary**: Autoregressive mesh generation using decoder-only transformers. Not voxel-native but demonstrates the GPT-style approach to 3D geometry.

## Key Patterns

| Approach | Token Type | Fixed Length? | Voxel-Native? |
|----------|-----------|---------------|---------------|
| ShapeGPT | VQ-VAE discrete (8x8x8) | Yes (512) | Yes |
| VoxSet | Sparse voxel + vector set | Yes | Yes |
| G3PT | Multi-scale discrete (LFQ) | Variable per scale | Partial |
| Apple Shape Tokens | Continuous vectors | Yes | No (point cloud) |
| LATTICE | Voxel-anchored latents | Yes | Yes |
| MeshGPT | Mesh face tokens | Variable | No |

## Additional Notable Work

### Michelangelo (NVIDIA) — Shape-Image-Text Aligned Generation
- **Date**: NeurIPS 2023
- **Link**: https://arxiv.org/abs/2306.17115
- **Key Idea**: Learns unified shape-image-text aligned latent space. Shape VQ-VAE tokenizes 3D point clouds/occupancy fields into discrete tokens via learned codebook. Directly voxel-compatible since occupancy representation is voxel-aligned.

### 3D-GPT: Procedural 3D Modeling with LLMs
- **Date**: NeurIPS 2023
- **Link**: https://arxiv.org/abs/2310.12945
- **Key Idea**: Not a tokenizer — uses LLMs as agents that output Blender Python scripts. Three agents: task dispatch, conceptualization, modeling. Code-generation approach rather than token-based.

### GPT4Point: Point-Language Understanding and Generation
- **Date**: CVPR 2024
- **Link**: https://arxiv.org/abs/2312.02980
- **Key Idea**: Extends GPT-4 paradigm to 3D point clouds. Point-QFormer aligns point features with LLM. VQ-VAE converts colored point clouds into discrete tokens for autoregressive generation. Trained on Objaverse.

### Point-Bind & Point-LLM
- **Date**: 2023
- **Link**: https://arxiv.org/abs/2309.00615
- **Key Idea**: Aligns 3D point clouds with images/text/audio via ImageBind-style joint embedding. Point-LLM connects 3D encoder to LLaMA for 3D QA and reasoning. Understanding-focused.

### AutoSDF — VQ-VAE on Voxel SDF Grids
- **Date**: CVPR 2022 (foundational)
- **Link**: https://arxiv.org/abs/2203.09516
- **Key Idea**: VQ-VAE on truncated signed distance fields (voxel grids of SDF values). Autoregressive transformer generates shape tokens. One of the earliest voxel-native tokenizers.

### CLAY (NVIDIA) — Multi-Resolution Voxel Hash Tokenization
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2406.13897
- **Key Idea**: VQ-VAE on multi-resolution voxel hashing (instant-NGP style). Diffusion transformer on latent tokens. One of the most voxel-native tokenization approaches at scale.

### XCube (NVIDIA) — Sparse Voxel Hierarchical Diffusion
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2312.03806
- **Key Idea**: Generates high-resolution 3D shapes using sparse voxel hierarchies with diffusion. Operates directly on voxel structures at up to 1024^3 resolution using sparse convolutions.

### OctFormer / OctFusion — Octree-Based Transformers
- **Date**: SIGGRAPH 2023 (OctFormer), 2024 (OctFusion)
- **Link**: https://arxiv.org/abs/2305.03045
- **Key Idea**: OctFormer applies transformers to octree-organized 3D data (hierarchical voxels). OctFusion combines octrees with diffusion. Octree nodes serialized via Morton/Z-order curves become natural token sequences.

### 3DShape2VecSet
- **Date**: SIGGRAPH 2023
- **Link**: https://arxiv.org/abs/2301.11445
- **Key Idea**: Encodes 3D shapes as sets of continuous latent vectors via cross-attention on query points. Not discrete tokenization, but a continuous "tokenization" approach.

### TRELLIS (Microsoft) — Structured Latents on Sparse Voxels
- **Date**: 2024
- **Link**: https://arxiv.org/abs/2412.01506
- **Key Idea**: Structured LATent (SLAT) representation — sparse voxel latent features decoded into various 3D outputs. One of the strongest open 3D generation models.

## Updated Comparison Table

| Approach | Representation | Token Type | Fixed Length? | Voxel-Native? |
|----------|---------------|-----------|---------------|---------------|
| ShapeGPT | Point cloud → 8x8x8 grid | VQ-VAE discrete | Yes (512) | Yes |
| VoxSet | Sparse voxel + vector set | Hybrid | Yes | Yes |
| G3PT | Multi-scale point data | LFQ discrete | Variable/scale | Partial |
| Apple Shape Tokens | Point cloud | Continuous vectors | Yes | No |
| LATTICE | Voxel-anchored latents | Continuous | Yes | Yes |
| MeshGPT | Mesh faces | VQ-VAE discrete | Variable | No |
| Michelangelo | Point cloud/occupancy | VQ-VAE discrete | Yes | Yes |
| GPT4Point | Colored point cloud | VQ-VAE discrete | Yes | Partial |
| AutoSDF | Voxel SDF grid | VQ-VAE discrete | Yes | **Yes** |
| CLAY | Multi-res voxel hash | VQ-VAE + diffusion | Yes | **Yes** |
| XCube | Sparse voxel hierarchy | Hierarchical diffusion | Variable | **Yes** |
| TRELLIS | Sparse voxel features | Structured latent | Variable | **Yes** |
| OctFormer | Octree (hierarchical voxel) | Window tokens | Variable | **Yes** |

## Implications for a Voxel Tool

- **VQ-VAE over voxel grids** (ShapeGPT style) is the simplest path to LLM-compatible voxel tokens
- **Sparse voxel representations** (VoxSet, LATTICE) are more efficient for larger/detailed models
- **Coarse-to-fine generation** (G3PT) maps well to octree-based voxel structures
- Standard LLMs (T5, GPT) can process 3D tokens without architecture changes — the tokenizer is the hard part
