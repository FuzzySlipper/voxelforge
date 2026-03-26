# 3D Dot Game Heroes & Voxel Frame Animation

## 3D Dot Game Heroes Technical Details

- **Developer**: Silicon Studio (published by FromSoftware, 2009, PS3)
- **Purpose**: Created in 10 months to showcase Silicon Studio's Orochi middleware engine
- **Grid**: 16x16x16 voxel grid per character
- **Colors**: 7 colors maximum, shared across all frames
- **Animation Frames**: 6 poses — Stand, Walk1, Walk2, Hooray, Attack1, Attack2
- **Editor Features**: Copy, paste, flip operations. Export to USB for sharing.
- **Rendering internals**: Unknown — no public GDC/CEDEC talks found, even searching in Japanese. The game predates the era of Japanese studios sharing technical details internationally.

Sources:
- [PlayStation Blog: Character Creation](https://blog.playstation.com/2010/04/20/3d-dot-game-heroes-character-creation-exposed/)
- [Engadget: Editor Explained](https://www.engadget.com/2010-02-16-3d-dot-game-heroes-character-editor-explained.html)
- [PlayStation Blog: Editor Details](https://blog.playstation.com/2010/02/16/3d-dot-game-heroes-arent-born-theyre-made-in-the-editor/)
- [Silicon Studio Wikipedia](https://en.wikipedia.org/wiki/Silicon_Studio)

## Voxel Frame-Swap Animation (General Technique)

The dominant approach for voxel character animation in games:

1. **Rig and animate** conventionally in external 3D software (Blender, etc.)
2. **Export each frame** as a separate mesh pose
3. **Voxelize each frame** independently
4. **Swap entire meshes** at runtime like a 3D sprite sheet (stop-motion style)

No dynamic blending between animations — intentional, matches the retro aesthetic.

### Performance Numbers (Daniel Schroeder's Voxel Renderer)

- **Voxelization speed**: Most meshes in under 1 second (multithreaded)
- **Memory**: 14-frame zombie walk cycle = 4.6 MB VRAM
- **Scaling estimate**: 20 enemy types × 200 frames each ≈ 1.3 GB total VRAM
- **Render perf**: 90+ FPS at 1440p on test hardware
- **Source**: https://blog.danielschroeder.me/blog/voxel-renderer-objects-and-animation/

### The Tooling Gap

Every source describes the same problem: **there are no good tools for frame-based voxel animation**. People cobble together MagicaVoxel for individual frames, then manually export and wire up. The tooling is described as the primary bottleneck — not the runtime rendering cost.

Relevant quote from the community: "Frame-Based animation is problematic because there are no existing tools that allow for the form to function effectively. There are methods using plug-ins and extensions within game engines, but no standalone applications comparable to tools like Aseprite or GraphicsGale for voxelart."

Sources:
- [Voxelart Styles in Video Games](https://www.gamedeveloper.com/art/voxelart-styles-in-video-games)
- [Sketchfab: Animating Voxels and Stop Motion](https://sketchfab.com/blogs/community/animating-voxels-sketchfab-stop-motion/)

## LLM-Assisted Voxel Animation Ideas

### Animation Retargeting via Semantic Understanding

Traditional animation retargeting requires matching transform/bone names and accounting for skeletal differences. With voxels + LLM:

- The LLM understands *conceptually* what a walk cycle is — which voxels are "legs moving"
- Given a walk animation on Model A (simple), it can identify analogous voxel regions on Model B (complex) and apply similar movements
- No bone name matching, no transform hierarchies — just spatial reasoning about which voxels should move where

### Tiered Authoring Pipeline

1. **Create animations on extremely simple voxel models first** (e.g., a basic humanoid stick figure in voxels)
2. **Create more complex character models** separately
3. **LLM retargets** the simple animations onto complex models by understanding the correspondence

This inverts the traditional pipeline where you rig first, animate second. Here you animate on cheap proxies and retarget via semantic understanding.

### Semantic Voxel Labels (Embedded Metadata)

Embed labels/tags into voxels on a reference frame:
- `right_leg`, `left_arm`, `torso`, `head` for characters
- `hilt`, `blade`, `pommel`, `guard` for a sword
- `roof`, `wall`, `door`, `window` for a building

These labels serve **dual purpose**:
1. **For the LLM**: Clear semantic anchors for animation manipulation, retargeting, and generation. "Generate swords with similar blades but all new hilts" becomes a tractable prompt because `blade` and `hilt` are explicitly labeled regions.
2. **For the user/tool**: Same labels are useful for manual editing, selection, LOD decisions, material assignment, etc.

This is essentially **bone data expressed as voxel metadata** — but more flexible because:
- It's human-readable (not a transform hierarchy)
- It can be arbitrarily granular (label individual voxels or regions)
- It works for non-character objects (weapons, buildings, vehicles)
- The LLM can both read and write these labels
- It enables compositional generation: "take the hilt from sword A, generate a new blade in the style of sword B"

### Variant Generation

With labeled voxel regions, generating variants becomes structured:
- "Generate 10 swords: keep the blade region similar, randomize the hilt region"
- "Make 5 color variants of this character: change armor voxels only"
- "Create a damaged version: remove/modify voxels in the chest region"

The labels turn open-ended generation into constrained generation — much more reliable for LLMs.
