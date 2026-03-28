# Winding Order in FNA / XNA4

## The Rule

**Clockwise winding = front face** when using FNA's default rasterizer state (`CullCounterClockwiseFace`).

This is the DirectX convention. OpenGL uses the opposite (CCW = front). If you're porting from MonoGame with OpenGL backend or from a pure OpenGL project, **all triangle winding must be reversed**.

## How FNA Culling Works

| Winding (from camera) | Face type | Default behavior |
|------------------------|-----------|------------------|
| Clockwise              | Front     | Rendered         |
| Counter-clockwise      | Back      | Culled           |

The default `RasterizerState.CullCounterClockwise` culls triangles whose projected vertices appear counter-clockwise from the camera's perspective. These are considered back-facing.

## Voxel Face Winding

For voxel meshers that emit quads (two triangles per face), the winding depends on which direction the face points:

- **Positive faces** (+X, +Y, +Z): The face normal points outward in the positive axis direction. Vertices must wind **clockwise** when viewed from outside (from the positive axis looking inward).

- **Negative faces** (-X, -Y, -Z): The face normal points outward in the negative axis direction. Vertices must wind **clockwise** when viewed from outside (from the negative axis looking inward). Because the viewing direction is flipped, this means the index order is **reversed** compared to positive faces.

Given 4 corner vertices (c0, c1, c2, c3) laid out as:
```
c3 --- c2
|      |
c0 --- c1
```

The triangle indices are:

```
Positive face: (0,1,2), (0,2,3)   // CW from outside
Negative face: (0,2,1), (0,3,2)   // CW from outside (reversed)
```

## Common Pitfalls

- **MonoGame OpenGL vs FNA**: MonoGame's OpenGL backend uses CCW as front face. If RuleWeaver or another MonoGame project migrates to FNA, all mesh winding needs to flip.

- **Assimp imports**: Models loaded via Assimp typically use CCW winding (3ds Max, Blender default). FNA's `CullCounterClockwiseFace` happens to render these correctly since the "wrong" front face becomes the visible face. If you switch to `CullClockwiseFace` or `CullNone`, imported models and voxel meshes will need consistent treatment.

- **Debugging**: If faces appear inside-out (you see interior but not exterior surfaces), the winding is backwards. Temporarily set `RasterizerState = RasterizerState.CullClockwise` or `CullNone` to confirm.

- **Lighting**: Even if faces render (e.g., with `CullNone`), incorrect winding causes normals to face the wrong way, making lighting look wrong. Fix the winding rather than disabling culling.
