namespace VoxelForge.App.Render;

// ── Top-level snapshot ──

/// <summary>
/// Canonical versioned render-scene snapshot DTO.
/// Renderer-neutral; produced by <see cref="Services.RenderSceneSnapshotService"/>.
/// Schema version: "voxelforge.render_scene@1".
/// </summary>
public sealed class RenderSceneSnapshot
{
    /// <summary>Schema version identifier for the snapshot contract.</summary>
    public required string SchemaVersion { get; init; } = "voxelforge.render_scene@1";

    /// <summary>Monotonic revision from the source workspace state.</summary>
    public required long Revision { get; init; }

    /// <summary>Stable model identifier.</summary>
    public required string ModelId { get; init; } = "default";

    /// <summary>Source metadata describing which host produced the snapshot.</summary>
    public required RenderSourceInfo Source { get; init; }

    /// <summary>Axis-aligned bounding box of voxel geometry only, or null if empty.</summary>
    public BoundsDto? Bounds { get; init; }

    /// <summary>Axis-aligned bounding box of reference geometry only, or null if empty.</summary>
    public BoundsDto? ReferenceBounds { get; init; }

    /// <summary>Combined axis-aligned bounding box of all geometry, or null if empty.</summary>
    public BoundsDto? CombinedBounds { get; init; }

    /// <summary>Voxel mesh data (greedy/naive mesher output).</summary>
    public required IReadOnlyList<RenderVoxelMesh> VoxelMeshes { get; init; } = [];

    /// <summary>Reference model render nodes (loaded 3D models).</summary>
    public required IReadOnlyList<RenderReferenceNode> ReferenceNodes { get; init; } = [];

    /// <summary>Materials referenced by meshes and primitives.</summary>
    public required IReadOnlyList<RenderMaterial> Materials { get; init; } = [];

    /// <summary>Textures referenced by materials.</summary>
    public required IReadOnlyList<RenderTexture> Textures { get; init; } = [];

    /// <summary>Palette entries (voxel material definitions).</summary>
    public required IReadOnlyList<RenderPaletteEntry> Palette { get; init; } = [];

    /// <summary>Diagnostic messages carried alongside the snapshot.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Source metadata identifying which transport/host produced this snapshot.
/// </summary>
public sealed class RenderSourceInfo
{
    /// <summary>Host identifier: "mcp", "bridge", or "test".</summary>
    public required string Host { get; init; } = "test";

    /// <summary>Capabilities advertised by this host.</summary>
    public required IReadOnlyList<string> Capabilities { get; init; } = [];
}

// ── Voxel mesh ──

/// <summary>
/// A single voxel mesh produced by the mesher.
/// </summary>
public sealed class RenderVoxelMesh
{
    /// <summary>Stable mesh identifier.</summary>
    public required string Id { get; init; } = "";

    /// <summary>Mesh revision (increments on mesh changes).</summary>
    public required long Revision { get; init; }

    /// <summary>Flat vertex positions: [x0, y0, z0, x1, y1, z1, ...].</summary>
    public required float[] Positions { get; init; } = [];

    /// <summary>Flat vertex normals: [nx0, ny0, nz0, ...].</summary>
    public required float[] Normals { get; init; } = [];

    /// <summary>Flat RGBA vertex colors as byte array.</summary>
    public required byte[] ColorsRgba { get; init; } = [];

    /// <summary>Per-vertex palette indices (one byte per vertex).</summary>
    public required byte[] PaletteIndices { get; init; } = [];

    /// <summary>Triangle index buffer.</summary>
    public required int[] Indices { get; init; } = [];

    /// <summary>Axis-aligned bounding box, or null if empty.</summary>
    public BoundsDto? Bounds { get; init; }

    /// <summary>Describes the payload encoding: "json_arrays" | "base64" | "binary".</summary>
    public required string PayloadFormat { get; init; } = "json_arrays";
}

// ── Reference nodes and primitives ──

/// <summary>
/// A loaded reference model as a render node.
/// </summary>
public sealed class RenderReferenceNode
{
    /// <summary>Stable node identifier.</summary>
    public required string Id { get; init; } = "";

    /// <summary>Human-readable display name.</summary>
    public required string DisplayName { get; init; } = "";

    /// <summary>Source format (e.g., "obj", "gltf", "fbx").</summary>
    public required string SourceFormat { get; init; } = "unknown";

    /// <summary>Source asset identifier, if available.</summary>
    public string? SourceAssetId { get; init; }

    /// <summary>Whether the node is visible in the scene.</summary>
    public required bool Visible { get; init; } = true;

    /// <summary>Render mode: "textured", "wireframe", "points", or "hidden".</summary>
    public required string RenderMode { get; init; } = "textured";

    /// <summary>World transform: position, rotation (degrees), scale.</summary>
    public required RenderTransform Transform { get; init; }

    /// <summary>Bounding box in local (untransformed) coordinates, or null.</summary>
    public BoundsDto? BoundsLocal { get; init; }

    /// <summary>Bounding box in world coordinates (after transform), or null.</summary>
    public BoundsDto? BoundsWorld { get; init; }

    /// <summary>Render primitives (mesh+material pairs) in this node.</summary>
    public required IReadOnlyList<RenderPrimitive> Primitives { get; init; } = [];

    /// <summary>Diagnostics for this reference node.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// A render primitive: mesh geometry + material assignment.
/// </summary>
public sealed class RenderPrimitive
{
    /// <summary>Stable primitive identifier.</summary>
    public required string Id { get; init; } = "";

    /// <summary>Index into the snapshot's materials array.</summary>
    public required int MaterialIndex { get; init; }

    /// <summary>Flat vertex positions: [x0, y0, z0, ...].</summary>
    public required float[] Position { get; init; } = [];

    /// <summary>Flat vertex normals: [nx0, ny0, nz0, ...].</summary>
    public required float[] Normal { get; init; } = [];

    /// <summary>Flat RGBA vertex colors, or null.</summary>
    public byte[]? ColorRgba { get; init; }

    /// <summary>UV sets for texture mapping.</summary>
    public required IReadOnlyList<RenderUvSet> UvSets { get; init; } = [];

    /// <summary>Triangle index buffer, or null for non-indexed geometry.</summary>
    public int[]? Indices { get; init; }

    /// <summary>Bounding box in local coordinates, or null.</summary>
    public BoundsDto? BoundsLocal { get; init; }
}

/// <summary>
/// A UV coordinate set for a primitive.
/// </summary>
public sealed class RenderUvSet
{
    /// <summary>UV set index (0 for the first set).</summary>
    public required int SetIndex { get; init; }

    /// <summary>Flat UV coordinates: [u0, v0, u1, v1, ...].</summary>
    public required float[] Uvs { get; init; } = [];

    /// <summary>UV origin convention.</summary>
    public required string Origin { get; init; } = "unknown";

    /// <summary>Whether the V coordinate is flipped.</summary>
    public required string FlipY { get; init; } = "asset_defined";
}

// ── Transform ──

/// <summary>
/// Spatial transform: translation + rotation (degrees) + uniform scale.
/// </summary>
public sealed class RenderTransform
{
    public required float PositionX { get; init; }
    public required float PositionY { get; init; }
    public required float PositionZ { get; init; }
    public required float RotationX { get; init; }
    public required float RotationY { get; init; }
    public required float RotationZ { get; init; }
    public required float Scale { get; init; } = 1f;
}

// ── Materials ──

/// <summary>
/// Material definition per the ADR contract.
/// Includes base color, textures, alpha mode, sidedness, color space.
/// </summary>
public sealed class RenderMaterial
{
    /// <summary>Stable material identifier.</summary>
    public required string Id { get; init; } = "";

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; } = "";

    /// <summary>RGBA base color factor: [r, g, b, a] in 0..1 range.</summary>
    public required double[] BaseColorFactor { get; init; } = [1.0, 1.0, 1.0, 1.0];

    /// <summary>Base color texture slot, or null.</summary>
    public RenderTextureSlot? BaseColorTexture { get; init; }

    /// <summary>Normal texture slot, or null.</summary>
    public RenderTextureSlot? NormalTexture { get; init; }

    /// <summary>Emissive texture slot, or null.</summary>
    public RenderTextureSlot? EmissiveTexture { get; init; }

    /// <summary>RGB emissive factor: [r, g, b] in 0..1 range.</summary>
    public double[]? EmissiveFactor { get; init; }

    /// <summary>Metallic factor: 0.0 (dielectric) to 1.0 (metal).</summary>
    public double MetallicFactor { get; init; }

    /// <summary>Roughness factor: 0.0 (smooth) to 1.0 (rough).</summary>
    public double RoughnessFactor { get; init; } = 1.0;

    /// <summary>Alpha rendering mode: "opaque", "mask", or "blend".</summary>
    public required string AlphaMode { get; init; } = "opaque";

    /// <summary>Alpha cutoff threshold for "mask" mode.</summary>
    public double? AlphaCutoff { get; init; }

    /// <summary>Whether the material is double-sided.</summary>
    public required bool DoubleSided { get; init; }

    /// <summary>Color space: "srgb", "linear", or "unknown".</summary>
    public required string ColorSpace { get; init; } = "unknown";

    /// <summary>Diagnostics for this material.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = [];
}

// ── Texture slots ──

/// <summary>
/// A texture slot referencing a texture by ID with UV/transform metadata.
/// </summary>
public sealed class RenderTextureSlot
{
    /// <summary>Reference to a texture ID in the snapshot's textures array.</summary>
    public required string TextureId { get; init; } = "";

    /// <summary>UV set index for this texture.</summary>
    public required int UvSet { get; init; }

    /// <summary>UV transform: offset, scale, rotation.</summary>
    public required RenderUvTransform UvTransform { get; init; }

    /// <summary>UV origin convention: "top_left", "bottom_left", "asset_defined", "unknown".</summary>
    public required string UvOrigin { get; init; } = "unknown";

    /// <summary>Whether to flip the V coordinate: boolean or "asset_defined".</summary>
    public required string FlipY { get; init; } = "asset_defined";

    /// <summary>Horizontal wrapping mode: "clamp", "repeat", "mirror", "unknown".</summary>
    public required string WrapS { get; init; } = "repeat";

    /// <summary>Vertical wrapping mode: "clamp", "repeat", "mirror", "unknown".</summary>
    public required string WrapT { get; init; } = "repeat";

    /// <summary>Source label describing where the texture was resolved: "assimp", "unity_sidecar", "manual_override", "generated", "unknown".</summary>
    public required string SourceLabel { get; init; } = "unknown";
}

/// <summary>
/// UV transform: offset, scale, rotation.
/// </summary>
public sealed class RenderUvTransform
{
    /// <summary>UV offset: [u, v].</summary>
    public required double[] Offset { get; init; } = [0.0, 0.0];

    /// <summary>UV scale: [u, v].</summary>
    public required double[] Scale { get; init; } = [1.0, 1.0];

    /// <summary>UV rotation in radians.</summary>
    public required double Rotation { get; init; }
}

// ── Textures ──

/// <summary>
/// A texture referenced by materials, with session-authorized URI.
/// </summary>
public sealed class RenderTexture
{
    /// <summary>Stable texture identifier.</summary>
    public required string Id { get; init; } = "";

    /// <summary>Session-authorized URI or transport handle for the texture data.</summary>
    public required string Uri { get; init; } = "";

    /// <summary>MIME type, e.g., "image/png".</summary>
    public string? MimeType { get; init; }

    /// <summary>Color space: "srgb", "linear", "unknown".</summary>
    public required string ColorSpace { get; init; } = "unknown";

    /// <summary>Texture width in pixels, or null.</summary>
    public int? Width { get; init; }

    /// <summary>Texture height in pixels, or null.</summary>
    public int? Height { get; init; }

    /// <summary>Diagnostics for this texture.</summary>
    public required IReadOnlyList<RenderDiagnostic> Diagnostics { get; init; } = [];
}

// ── Palette entries ──

/// <summary>
/// A voxel palette entry, representing one voxel material.
/// </summary>
public sealed class RenderPaletteEntry
{
    /// <summary>Palette index (1-255; 0 reserved for air).</summary>
    public required byte Index { get; init; }

    /// <summary>Material name.</summary>
    public required string Name { get; init; } = "";

    /// <summary>RGBA color components.</summary>
    public required byte R { get; init; }
    public required byte G { get; init; }
    public required byte B { get; init; }
    public required byte A { get; init; } = 255;

    /// <summary>Whether this palette entry is visible in voxel rendering.</summary>
    public required bool Visible { get; init; } = true;
}

// ── Shared DTOs ──

/// <summary>
/// Axis-aligned bounding box with double-precision coordinates.
/// </summary>
public sealed class BoundsDto
{
    public required double MinX { get; init; }
    public required double MinY { get; init; }
    public required double MinZ { get; init; }
    public required double MaxX { get; init; }
    public required double MaxY { get; init; }
    public required double MaxZ { get; init; }
}

/// <summary>
/// A diagnostic message carried alongside a snapshot or sub-element.
/// </summary>
public sealed class RenderDiagnostic
{
    /// <summary>Diagnostic severity: "info", "warning", "error".</summary>
    public required string Severity { get; init; } = "info";

    /// <summary>Category or source of the diagnostic.</summary>
    public required string Category { get; init; } = "";

    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; } = "";
}
