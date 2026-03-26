# Medical Imaging: LLMs + Voxel Data (2024–2026)

Medical imaging (CT, MRI) is inherently voxel-based — 3D volumes of discrete measurements. This is a parallel track to creative/game voxel work, with different tools and communities, but shared underlying concepts.

## Key Systems

### VoxelPrompt: A Vision-Language Agent for Grounded Medical Image Analysis
- **Date**: October 2024
- **Link**: https://arxiv.org/html/2410.08397v1
- **What it does**: Processes volumetric (3D) medical scans in response to text prompts. Outputs language, volumes, and computed metrics. A single model can localize anatomical regions, perform measurements, and characterize image features in open language.

### M3D-LaMed: A Generalist MLLM for 3D Medical Image Analysis
- **Date**: 2024 (ICLR 2025)
- **Link**: https://openreview.net/forum?id=XQL4Pmf6m6
- **What it does**: Multimodal LLM specializing in 8 tasks: image-text retrieval, report generation, VQA, positioning, segmentation, and more. Works directly with 3D medical volumes.

### CT-Agent: A Multimodal-LLM Agent for 3D CT Radiology QA
- **Date**: May 2025
- **Link**: https://arxiv.org/html/2505.16229v1
- **What it does**: LLM agent that answers questions about 3D CT scans. Uses region representative token pooling to extract 3D CT features from 2D-pretrained vision models.

### BrainGPT: 3D Brain CT Report Generation
- **Date**: 2025
- **Link**: https://www.nature.com/articles/s41467-025-57426-0 (Nature Communications)
- **What it does**: Clinically-tuned model for generating radiology reports from 3D brain CT scans. Trained on 3D-BrainCT dataset (18,885 text-scan pairs).

### CT-RATE Dataset
- **What it does**: Large-scale dataset of 50,188 raw chest CT volumes from 21,304 patients. Foundation for training medical 3D LLMs.

### Integrating CT Reconstruction, Segmentation, and LLMs
- **Date**: 2025
- **Link**: https://link.springer.com/article/10.1007/s11517-025-03446-3
- **What it does**: End-to-end pipeline from CT image reconstruction through segmentation to LLM-generated diagnostic insights.

## Relevance to Creative Voxel Tools

While medical voxel work operates in a different domain, there are transferable ideas:

1. **3D feature extraction**: Methods for processing volumetric data (like region token pooling) could apply to any voxel grid
2. **Voxel-level semantic understanding**: VoxelPrompt's approach of grounding language in voxel space maps to creative use cases (e.g., "make the roof red" in a voxel building)
3. **Multi-resolution processing**: Medical imaging handles very large volumes (512x512x300+) — techniques for this scale could benefit large voxel scenes
4. **Sparse representation**: Medical volumes are mostly empty/homogeneous — same as creative voxel models. Sparse processing techniques transfer directly.

## Challenge

The main challenge in medical imaging is that 3D data is expensive to process — current methods often slice 3D into 2D for processing, losing voxel-level spatial continuity. This is the same fundamental challenge for creative voxel+LLM tools.
