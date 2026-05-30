# Reference State Preset Schema (V1)

## Purpose

`ReferenceStatePreset` provides a durable, versioned, human/agent-readable JSON
schema for exporting and importing all loaded reference model state as a single
reloadable bundle. This is the **multi-model** preset system, distinct from the
per-model `ReferenceModelMeta` (`.refmeta`) sidecar format.

## File extension

`.vf-state-preset.json` is the recommended naming convention for preset files.
The `export_reference_state_preset` MCP tool appends this extension if none is
given.

## Schema envelope

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | yes | Current: `1`. Incremented on breaking changes. |
| `label` | string or null | no | Human-readable label for this preset. |
| `notes` | string or null | no | Provenance notes, workflow context, or agent instructions. |
| `createdAt` | ISO 8601 string or null | no | Timestamp of preset creation. |
| `createdBy` | string | no | Tool or agent that created this preset. Default: `"VoxelForge"`. |
| `entries` | array of entry objects | yes | Reference model entries. |

## Entry object

Each entry represents one loaded reference model.

### Source

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `sourcePath` | string | **yes** | Path to the source 3D model file. **V1 uses absolute paths only.** |
| `format` | string | no | File format hint (e.g. "FBX", "OBJ", "GLTF"). |
| `importSourceLabel` | string or null | no | Import/source pipeline label (e.g. "assimp", "unity_sidecar"). |

### Transform

All transform fields are optional floats defaulting to 0 (position/rotation) or
1 (scale). Undefined values are serialized as their floats but omitted if null.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `positionX` | number | 0 | World-space X position. |
| `positionY` | number | 0 | World-space Y position. |
| `positionZ` | number | 0 | World-space Z position. |
| `rotationX` | number | 0 | Rotation around X axis in degrees. |
| `rotationY` | number | 0 | Rotation around Y axis in degrees. |
| `rotationZ` | number | 0 | Rotation around Z axis in degrees. |
| `scale` | number | 1 | Uniform scale factor. |

### Visibility and Render Mode

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `isVisible` | boolean | true | Whether the model is visible in the viewer. |
| `renderMode` | string | "solid" | One of: `"solid"`, `"wireframe"`, `"transparent"`. |

### Animation State

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `activeClipIndex` | integer or null | null | Index of the currently active animation clip. |
| `animationSpeed` | number | 1 | Playback speed multiplier. |
| `hasAnimations` | boolean | false | Whether the source model has animation data. |

### Per-Mesh Overrides

An array of override objects, one per mesh. Omitted if all meshes use defaults.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `meshIndex` | integer | 0 | Mesh index within the model. |
| `materialName` | string or null | null | Material name from the source model. |

**Texture paths** — these are absolute paths. If the file does not exist at
import time, a warning is emitted but the path is still stored for reference.

| Field | Type | Description |
|-------|------|-------------|
| `diffuseTexturePath` | string or null | Diffuse texture from import/sidecar. |
| `emissiveTexturePath` | string or null | Emissive texture from import/sidecar. |
| `emissiveBrightness` | number or null | Emissive brightness multiplier. |
| `manualDiffuseOverridePath` | string or null | Manual override for diffuse (session-only overrides persisted for reload). |
| `manualNormalOverridePath` | string or null | Manual override for normal map. |
| `manualEmissiveOverridePath` | string or null | Manual override for emissive. |

**Texture sampling controls** — only present when different from defaults.

| Field | Type | Default | Valid values |
|-------|------|---------|--------------|
| `uvOrigin` | string | `"top_left"` | `"top_left"`, `"bottom_left"`, `"asset_defined"` |
| `flipY` | string | `"asset_defined"` | `"true"`, `"false"`, `"asset_defined"` |
| `wrapS` | string | `"repeat"` | `"clamp"`, `"repeat"`, `"mirror"` |
| `wrapT` | string | `"repeat"` | `"clamp"`, `"repeat"`, `"mirror"` |
| `samplingControlsSource` | string | `"assimp"` | `"assimp"`, `"unity_sidecar"`, `"manual_sampling_override"`, `"vf_reference_settings"` |

### Provenance

| Field | Type | Description |
|-------|------|-------------|
| `provenance` | string or null | Free-form provenance or workflow context for this entry. |

## V1 limitations

- **Absolute paths only.** Project-relative path resolution is not implemented
  in V1. All `sourcePath` and texture paths are stored as absolute filesystem
  paths. This means preset files are not portable across machines without path
  editing.
- **No backward compatibility.** V2+ schemas will need explicit migration. The
  `schemaVersion` field provides the compatibility check.
- **No binary mesh data.** Only configuration state is preserved. Mesh geometry
  must be re-loaded from the original source files.
- **No per-mesh vertex color overrides.** Only texture overrides are captured.

## Future considerations (V2+)

- Project-relative path resolution with a `basePath` field in the envelope.
- Relative path normalization on export.
- Per-mesh vertex color overrides.
- Multi-resolver path lookup (project dir, asset library, etc.).

## MCP tools

| Tool | Description |
|------|-------------|
| `export_reference_state_preset` | Export all loaded reference models to a `.vf-state-preset.json` file. |
| `import_reference_state_preset` | Load a preset file and restore all reference models. Supports `clear_existing` to replace current models. |

## Example file

See `tests/fixtures/am-golem-reference-state-preset.json` for a realistic
fixture inspired by the AM Golem messy asset pattern with:
- Full transform state
- Per-mesh diffuse and emissive texture paths
- Manual normal override
- Bottom-left UV origin (Unity convention)
- Clamped wrapping for specific meshes
- Active animation state
- Provenance/notes
