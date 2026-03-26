# Open Source Tools: Voxels + LLMs (2023–2026)

## Text-to-Voxel Pipelines

### text2vox
- **Link**: https://github.com/gfodor/text2vox | [Replicate](https://replicate.com/gfodor/text2vox)
- **What it does**: Cog service that generates MagicaVoxel .vox files from text prompts. Pipeline: text → Flux Dev (image generation) → Hunyuan3D-2 (3D reconstruction) → voxelization → .vox output.
- **Output**: MagicaVoxel VOX files at configurable resolutions (high/low detail)
- **Stack**: Python, Cog (containerized ML), runs on Replicate

### text2voxels
- **Link**: https://github.com/neverix/text2voxels
- **What it does**: Generate 3D voxels from text with AI. Simpler pipeline than text2vox.

### Text-to-Voxel 3D Model Generator (GetLLMs)
- **Link**: https://getllms.org/models/text-to-voxel-3d-model-generator
- **What it does**: Converts text prompts into MagicaVoxel VOX format with adjustable detail levels and resolutions. Uses Flux + Hunyuan3D-2 pipeline.

## 3D Generation Frameworks (Voxel-Adjacent)

### Shap-E (OpenAI)
- **Link**: https://github.com/openai/shap-e | [HuggingFace](https://huggingface.co/openai/shap-e)
- **What it does**: Generates 3D objects conditioned on text or images. Outputs implicit functions renderable as textured meshes AND neural radiance fields. Generates in seconds.
- **Voxel relevance**: Can be combined with voxelization to produce voxel output. Faster than Point-E.

### Point-E (OpenAI)
- **Link**: https://openai.com/index/point-e/
- **What it does**: Text → synthetic image → 3D point cloud via two-stage diffusion. Point clouds can be voxelized.

### LATTICE (CVPR 2026)
- **Link**: https://github.com/Zeqiang-Lai/LATTICE
- **What it does**: High-fidelity 3D generation using VoxSet representation (latent vectors anchored to coarse voxel grid). Open source. State-of-the-art quality.

### threestudio
- **What it does**: Unified framework for 3D generation from text/images. Supports DreamFusion, Score Jacobian Chaining, and other methods. Can output voxelized results.

## Voxel Editors with AI

### VoxiGen
- **Link**: https://voxigen.io/en | [three.js forum](https://discourse.threejs.org/t/voxigen-ai-powered-browser-voxel-editor-generate-edit-3d-models-from-text-images/89926)
- **What it does**: Free, browser-based voxel editor with AI-powered generation from text/images. Built on Three.js. Selected for ALPHA startup program at Web Summit.
- **Date**: Announced February 2026

### StableGen (Blender Addon)
- **Link**: https://github.com/sakalond/StableGen
- **What it does**: Generative AI texturing workflow within Blender. Can be combined with Blender's voxel remesh tools.

## LLM Code Generation for 3D

### PromptSCAD
- **Link**: https://promptscad.com/
- **What it does**: AI-powered OpenSCAD assistant generating 3D-printable parametric SCAD code from natural language. Models can be voxelized post-generation.

### Build Great AI (LLM → OpenSCAD → STL)
- **Link**: https://www.zenml.io/llmops-database/llm-powered-3d-model-generation-for-3d-printing
- **What it does**: Prototype using LLaMA 3.1, GPT-4, and Claude 3.5 to generate OpenSCAD code, converted to STL for 3D printing.

## Voxel Conversion Tools

### Tripo3D Voxel Converter Guide
- **Link**: https://www.tripo3d.ai/content/en/use-case/the-best-voxel-model-converter
- **What it does**: Guide to best voxel model converters of 2025, including AI-assisted workflows.

### Codrops: Turning 3D Models to Voxel Art with Three.js
- **Link**: https://tympanus.net/codrops/2023/03/28/turning-3d-models-to-voxel-art-with-three-js/
- **What it does**: Tutorial/code for converting arbitrary 3D models into voxel art in the browser using Three.js.

## Voxel Generation Libraries

### voxgen
- **Link**: https://github.com/wodend/voxgen
- **What it does**: Procedural voxel generation library.

### VOX4U
- **Link**: https://github.com/mik14a/VOX4U
- **What it does**: MagicaVoxel VOX format import plugin for Unreal Engine 5.
