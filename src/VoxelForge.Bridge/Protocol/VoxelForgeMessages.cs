namespace VoxelForge.Bridge.Protocol;

/// <summary>
/// VoxelForge-specific bridge protocol messages for mesh snapshots,
/// palette queries, and schema handshake.
/// These are read-only snapshot commands per the bridge protocol doc;
/// TS owns rendering/presentation only.
/// </summary>

// ── VoxelForge Schema Handshake ──

/// <summary>
/// VoxelForge schema handshake request sent by TS after
/// the den-bridge transport handshake completes.
/// </summary>
public sealed class VoxelForgeHandshakeRequest
{
    public string ClientSchemaVersion { get; set; } = "voxelforge@1";
    public string[] SupportedCapabilities { get; set; } = ["mesh_json"];
}

/// <summary>
/// VoxelForge schema handshake response from the C# sidecar.
/// Declares supported capabilities and schema version.
/// </summary>
public sealed class VoxelForgeHandshakeResponse
{
    public required string SidecarSchemaVersion { get; set; }
    public required string[] SupportedCapabilities { get; set; }
    public required bool Compatible { get; set; }
    public required string SchemaBundleId { get; set; }
}

// ── Mesh Snapshot ──

/// <summary>
/// Read-only request for a mesh snapshot of the current (or specified) model.
/// TS-owned request per the bridge protocol: TS asks for mesh data,
/// C# responds with authoritative mesh geometry.
/// </summary>
public sealed class MeshSnapshotRequest
{
    /// <summary>
    /// Model identifier. Empty or null means the currently active model.
    /// The only supported value for this vertical slice is "default"
    /// (loads the built-in sample model).
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Level-of-detail hint. 0 means full detail.
    /// </summary>
    public int LodLevel { get; set; }

    /// <summary>
    /// Payload format: "json" (only format supported in this vertical slice).
    /// </summary>
    public string PayloadFormat { get; set; } = "json";

    /// <summary>
    /// Whether to include palette mapping in the response.
    /// </summary>
    public bool IncludePaletteMapping { get; set; } = true;
}

/// <summary>
/// Mesh snapshot response carrying renderer-neutral mesh geometry
/// suitable for upload to any GPU API.
/// </summary>
public sealed class MeshSnapshotResponse
{
    public required string ModelId { get; set; }
    public required string MeshId { get; set; }
    public required string Format { get; set; }

    public int VertexCount { get; set; }
    public int IndexCount { get; set; }
    public int TriangleCount { get; set; }

    /// <summary>
    /// Flat array of float32 vertex positions: [x0, y0, z0, x1, y1, z1, ...].
    /// </summary>
    public required float[] Positions { get; set; }

    /// <summary>
    /// Flat array of float32 vertex normals: [nx0, ny0, nz0, ...].
    /// </summary>
    public required float[] Normals { get; set; }

    /// <summary>
    /// Flat array of uint8 RGBA vertex colors: [r0, g0, b0, a0, ...].
    /// </summary>
    public required byte[] Colors { get; set; }

    /// <summary>
    /// Per-vertex palette indices (one byte per vertex). May be null.
    /// </summary>
    public byte[]? PaletteIndices { get; set; }

    /// <summary>
    /// Triangle index buffer as uint32 values stored in int array.
    /// </summary>
    public required int[] Indices { get; set; }

    /// <summary>
    /// Axis-aligned bounding box, null if empty model.
    /// </summary>
    public BoundsDto? Bounds { get; set; }

    /// <summary>
    /// Palette mapping: palette index → material info.
    /// Included when IncludePaletteMapping is true.
    /// </summary>
    public Dictionary<string, PaletteEntryDto>? PaletteMapping { get; set; }

    /// <summary>
    /// Performance metrics for the snapshot generation.
    /// </summary>
    public MeshSnapshotMetrics? Metrics { get; set; }
}

public sealed class BoundsDto
{
    public required int MinX { get; set; }
    public required int MinY { get; set; }
    public required int MinZ { get; set; }
    public required int MaxX { get; set; }
    public required int MaxY { get; set; }
    public required int MaxZ { get; set; }
}

public sealed class PaletteEntryDto
{
    public required string Name { get; set; }
    public required string Color { get; set; } // "#RRGGBB" hex format
    public byte A { get; set; } = 255;
    public bool Visible { get; set; } = true;
}

public sealed class MeshSnapshotMetrics
{
    public required long MeshGenerationMs { get; set; }
    public required long SerializationMs { get; set; }
    public required long TotalMs { get; set; }
}

// ── Palette Get ──

/// <summary>
/// Read-only request for the current palette definition.
/// C#-owned response per the bridge protocol.
/// </summary>
public sealed class PaletteGetRequest
{
    /// <summary>
    /// Model identifier. Empty or null means the currently active model.
    /// </summary>
    public string ModelId { get; set; } = "";
}

/// <summary>
/// Palette response with authoritative palette entries.
/// </summary>
public sealed class PaletteGetResponse
{
    public required string PaletteId { get; set; }
    public required PaletteEntryResponse[] Entries { get; set; }
    public required int EntryCount { get; set; }
}

public sealed class PaletteEntryResponse
{
    public required byte Index { get; set; }
    public required string Name { get; set; }
    public required string Color { get; set; } // "#RRGGBB" hex
    public required byte A { get; set; }
    public required bool Visible { get; set; }
}