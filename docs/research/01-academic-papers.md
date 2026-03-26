# Academic Papers: Voxels + LLMs (2023–2026)

## Voxel Generation & Editing

### VP-LLM: Text-Driven 3D Volume Completion with Large Language Models through Patchification
- **Date**: September 2024 (submitted to ICLR 2025)
- **Link**: https://openreview.net/forum?id=JmXu4fk5Mm | [arXiv](https://arxiv.org/html/2406.05543)
- **Summary**: Uses LLMs for conditional 3D volume completion and denoising via a token-based single-forward-pass approach. Demonstrates that LLMs can interpret complex text instructions and comprehend 3D objects, surpassing diffusion-based 3D completion models on complex text prompts.

### Vox-E: Text-guided Voxel Editing of 3D Objects
- **Date**: March 2023 (ICCV 2023)
- **Link**: https://tau-vailab.github.io/Vox-E/ | [arXiv](https://arxiv.org/abs/2303.12048)
- **Summary**: Models a scene as a voxel grid with learned features, then performs text-guided editing via score distillation loss. Supports both local and global appearance/geometry modifications guided solely by text prompts. Uses pre-trained 2D diffusion models adapted for 3D.

### LLMto3D: Generation of Parametric, 3D Printable Objects Using Large Language Models
- **Date**: 2025
- **Link**: https://journals.sagepub.com/doi/10.1177/14780771251353792
- **Summary**: Explores using LLMs to generate parametric 3D-printable objects. Bridges language models with physical fabrication through code generation.

### ShapeLLM-Omni: A Native Multimodal LLM for 3D Generation and Understanding
- **Date**: 2025
- **Link**: https://arxiv.org/html/2506.01853v1
- **Summary**: Introduces a 3D VQVAE module encoding 3D shapes into discrete representations, enabling autoregressive models to perform unified multimodal understanding and generation across text, images, and 3D content.

### SceneCraft: Layout-Guided 3D Scene Generation (NeurIPS 2024)
- **Date**: October 2024
- **Link**: https://arxiv.org/html/2410.09049v1 | [GitHub](https://github.com/OrangeSodahub/SceneCraft)
- **Summary**: Generates detailed indoor scenes from text descriptions and spatial layouts. Converts 3D semantic layouts into multi-view 2D proxy maps. **Voxel connection**: enhances bounding boxes by voxelizing them into fine-grained voxels to capture complex geometries.

### SceneCraft: An LLM Agent for Synthesizing 3D Scene as Blender Code
- **Date**: March 2024
- **Link**: https://arxiv.org/abs/2403.01248
- **Summary**: Different paper, same name. An LLM agent that converts text descriptions into Blender-executable Python scripts to render complex scenes with up to 100 3D assets.

### LayoutGPT: Compositional Visual Planning and Generation with LLMs
- **Date**: 2024 (CVPR 2024)
- **Summary**: Uses LLMs for compositional layout generation in 2D and 3D scenes. Demonstrates that LLMs can perform spatial reasoning for scene composition.

## 3D Understanding & Language Grounding

### Scene-LLM: Extending Language Model for 3D Visual Reasoning
- **Date**: 2025 (WACV 2025)
- **Link**: https://openaccess.thecvf.com/content/WACV2025/papers/Fu_Scene-LLM_Extending_Language_Model_for_3D_Visual_Reasoning_WACV_2025_paper.pdf
- **Summary**: Extends language models for 3D visual reasoning tasks, processing 3D scenes and answering spatial questions about them.

### VoxRep: Enhancing 3D Spatial Understanding in 2D VLMs via Voxel Representation
- **Date**: March 2025
- **Link**: https://arxiv.org/html/2503.21214v1
- **Summary**: Demonstrates that 2D Vision-Language Models can learn voxel representations through a data adaptation strategy that flattens 3D voxel grids into 2D image slices.

### NeuroVoxel-LM: Language-Aligned 3D Perception via Dynamic Voxelization and Meta-Embedding
- **Date**: July 2025
- **Link**: https://arxiv.org/html/2507.20110v1
- **Summary**: Integrates NeRF with dynamic resolution voxelization and meta-embedding. Adaptively modifies voxel granularity based on structural and geometric complexity for language-aligned 3D perception.

### 3DGraphLLM: Combining Semantic Graphs and Large Language Models for 3D
- **Date**: 2025 (ICCV 2025)
- **Link**: [GitHub](https://github.com/CognitiveAISystems/3DGraphLLM) | [OpenReview](https://openreview.net/forum?id=or9OfAC3kb)
- **Summary**: Uses 3D scene graphs with LLMs for referred object grounding. Achieves +7.5% F1@0.5 on Multi3DRefer and +6.4% Acc@0.5 on ScanRefer.

### POP-3D: Open-Vocabulary 3D Occupancy Prediction from Images
- **Date**: 2024
- **Link**: https://vobecant.github.io/POP3D/
- **Summary**: Predicts open-vocabulary 3D semantic voxel occupancy maps from 2D images. Enables 3D grounding, segmentation, and retrieval of free-form language queries through voxel-level occupancy.

### O-Voxel: Advanced 3D Scene Encoding
- **Link**: https://www.emergentmind.com/topics/o-voxel-representation
- **Summary**: Advanced 3D scene encoding approach using optimized voxel representations.

## CAD Code Generation (Voxel-Adjacent)

### Generating CAD Code with Vision-Language Models for 3D Designs
- **Date**: October 2024
- **Link**: https://arxiv.org/html/2410.05340v2
- **Summary**: Uses VLMs to generate CAD code (OpenSCAD/CadQuery) for 3D designs from images and text.

### EvoCAD: Evolutionary CAD Code Generation with Vision Language Models
- **Date**: 2025
- **Link**: https://arxiv.org/pdf/2510.11631
- **Summary**: Evolutionary approach to CAD code generation using VLMs, iteratively improving designs.

### AIDL: AI Design Language (MIT CSAIL + Adobe)
- **Date**: 2024-2025
- **Summary**: A domain-specific language designed for LLM-driven 3D design, with features like implicit geometry referencing and constraint declaration. Outperforms standard OpenSCAD for LLM-generated shapes.

## Surveys

### When LLMs Step into the 3D World: A Survey and Meta-Analysis of 3D Tasks via Multi-modal Large Language Models
- **Link**: https://www.themoonlight.io/en/review/when-llms-step-into-the-3d-world-a-survey-and-meta-analysis-of-3d-tasks-via-multi-modal-large-language-models
- **Summary**: Comprehensive survey of LLM applications in 3D tasks.

### Awesome 3D Scene Generation (curated paper list)
- **Link**: https://github.com/hzxie/Awesome-3D-Scene-Generation
- **Summary**: Curated list of 3D scene generation papers, many involving LLMs and voxel representations.
