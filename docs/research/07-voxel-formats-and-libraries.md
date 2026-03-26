# Voxel Formats, Libraries, and Serialization (2023–2026)

## Voxel File Formats

| Format | Extension | Tool | Notes |
|--------|-----------|------|-------|
| MagicaVoxel VOX | `.vox` | MagicaVoxel | Most common voxel art format. Well-documented. Palette-based (256 colors). Multiple models per file. [Spec](https://github.com/ephtracy/voxel-model/blob/master/MagicaVoxel-file-format-vox-extension.txt) |
| OpenVDB | `.vdb` | Houdini, Blender | Industry standard for volumetric data. Sparse hierarchical data structure. Used in VFX. Overkill for simple voxel art. |
| binvox | `.binvox` | binvox tool | Simple binary voxel format. Good for boolean occupancy grids. |
| NRRD | `.nrrd` | Medical imaging | N-dimensional raster data. Used for CT/MRI volumes. |
| Minecraft Schematic | `.schem`, `.schematic`, `.nbt` | Minecraft | WorldEdit/Litematica formats. NBT-based. Large community of tools. |
| VoxEdit | `.vxm` | The Sandbox | Proprietary format for The Sandbox game. |
| Qubicle | `.qb`, `.qbt` | Qubicle | Commercial voxel editor format. |
| USD/USDZ | `.usd`, `.usdz` | Pixar/Apple ecosystem | Can represent voxel data via VDB or point instancing |
| glTF | `.gltf`, `.glb` | Web/engines | Mesh format, but voxel models exported as instanced cubes |

### Format Considerations for LLM Tools
- **.vox** is the best target for LLM-generated voxels — simple, well-documented, palette-based
- **Minecraft schematics** have the largest existing community and LLM agent ecosystem
- **.obj/.glb** work as interchange but lose voxel-native properties (palette, grid alignment)
- **OpenVDB** is too complex for most LLM use cases

## Python Libraries for Voxel Work

### trimesh (voxel module)
- **Link**: https://trimesh.org/trimesh.voxel.html
- **Capabilities**: Mesh → voxel conversion, voxel grid operations, encoding/decoding
- **Best for**: Voxelizing existing meshes programmatically

### PyVista
- **Link**: https://pyvista.org/ | [Tutorial](https://tutorial.pyvista.org/)
- **Capabilities**: Voxelization, slicing, resampling, ray-tracing, visualization. 2000+ projects use it.
- **Best for**: Scientific visualization, analysis of voxel data

### Open3D
- **Capabilities**: Voxelization, point cloud → voxel, voxel downsampling, 3D deep learning support
- **Best for**: ML/deep learning pipelines with voxel data

### pyntcloud
- **Capabilities**: Voxelization, feature extraction, point cloud processing
- **Best for**: Lightweight voxel preprocessing for ML

### py-vox-io / pyvox
- **What it does**: Read/write MagicaVoxel .vox files in Python
- **Best for**: Direct .vox file manipulation from LLM-generated code

### numpy + scipy
- **What it does**: Voxel grids are just 3D arrays — numpy is the foundation for any custom voxel work
- **Best for**: Custom voxel operations, morphological operations (scipy.ndimage)

## JavaScript/WebGL Voxel Libraries

### Three.js
- **Link**: https://threejs.org/examples/webgl_interactive_voxelpainter.html
- **Capabilities**: Built-in voxel painter example, instanced mesh rendering for voxels
- **Used by**: VoxiGen, countless voxel web editors

### Babylon.js
- **Capabilities**: Similar to Three.js, supports instanced rendering for voxel display

### vox-reader / three-voxel-loader
- **What it does**: Load MagicaVoxel .vox files directly into Three.js scenes

## Rust Voxel Libraries

### dot_vox
- **What it does**: Read/write MagicaVoxel .vox files in Rust. Well-maintained.

### block-mesh-rs (formerly building-blocks)
- **What it does**: High-performance voxel meshing (greedy meshing, surface nets)

### bevy ecosystem
- **What it does**: Bevy game engine has multiple voxel plugins (`bevy_voxel_world`, etc.)

### veloren
- **What it does**: Open-source voxel RPG in Rust — good reference implementation for voxel engines

## Minecraft-Specific Libraries

### amulet-core (Python)
- **What it does**: Reads all Minecraft world/schematic formats. Most comprehensive Minecraft data library.

### nbtlib (Python)
- **What it does**: Read/write Minecraft NBT format

### prismarine-nbt (JavaScript)
- **What it does**: NBT parsing for the Mineflayer ecosystem

### fastnbt (Rust)
- **What it does**: Fast NBT serialization/deserialization

### MCSchematic (Python)
- **What it does**: Read/write Minecraft .schematic files. Often combined with LLM code generation for build pipelines.

## Representing Voxels as Text for LLMs

### Direct Serialization Approaches
1. **JSON array**: `[[x,y,z,color], ...]` — simple but verbose for large models
2. **Run-length encoding**: Compress sparse grids into text-friendly format
3. **Layer-by-layer**: Serialize each Y-layer as a 2D grid (like Minecraft structure blocks)
4. **Palette + grid**: Separate color palette from spatial data
5. **Octree text**: Hierarchical description of filled/empty regions

### Token-Based Approaches (from research)
- **VQ-VAE tokens**: ShapeGPT uses 512 tokens from 8x8x8 grid
- **Multi-scale tokens**: G3PT uses coarse-to-fine discrete tokens
- **Continuous embeddings**: Apple Shape Tokens as float vectors
- **Sparse voxel tokens**: VoxSet anchors tokens to occupied voxels only

### Additional Serialization Strategies
6. **Minecraft command format**: `/setblock x y z block_type` — LLMs already know this from training data
7. **SVG-like layer descriptions**: Describe each layer using shape primitives (fill rect, draw line) rather than individual voxels — far more token-efficient
8. **Hash map**: `{(x,y,z): block_type}` — Python dict representation is very LLM-friendly
9. **Morton/Z-order curve**: Sorted coordinates for spatial locality
10. **Base64-encoded binary**: For transferring data through context without many tokens (LLM can't reason about contents)

### Practical Considerations
- Small voxel models (16x16x16 = 4096 voxels) fit easily in LLM context
- Medium models (32x32x32 = 32K) are feasible with sparse representation
- Large models (64x64x64 = 262K) require compression/tokenization
- LLMs work best with **structured text** (JSON, code) rather than raw binary data
- **Code generation** (Python/JS that creates voxels procedurally) is often more practical than direct voxel data generation
