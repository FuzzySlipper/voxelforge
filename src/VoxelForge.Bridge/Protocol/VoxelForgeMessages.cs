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

// ── Mesh Subscribe / Unsubscribe ──

/// <summary>
/// TS-owned request to subscribe to mesh update events for a model.
/// After subscribing, the sidecar pushes voxelforge.mesh.update events
/// whenever the model's mesh changes.
/// </summary>
public sealed class MeshSubscribeRequest
{
    /// <summary>
    /// Model identifier. Empty or null means the currently active model.
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Chunk size for region-based updates. Default 16.
    /// </summary>
    public int ChunkSize { get; set; } = 16;

    /// <summary>
    /// Whether to receive an immediate full snapshot on subscribe.
    /// </summary>
    public bool SendFullSnapshotOnSubscribe { get; set; } = true;
}

/// <summary>
/// C#-owned response acknowledging mesh subscription.
/// </summary>
public sealed class MeshSubscribeResponse
{
    public required string ModelId { get; set; }
    public required string SubscriptionId { get; set; }
    public required int ChunkSize { get; set; }

    /// <summary>
    /// If SendFullSnapshotOnSubscribe was true, this contains the initial snapshot.
    /// </summary>
    public MeshSnapshotResponse? InitialSnapshot { get; set; }
}

/// <summary>
/// TS-owned request to unsubscribe from mesh update events.
/// </summary>
public sealed class MeshUnsubscribeRequest
{
    public required string SubscriptionId { get; set; }
}

/// <summary>
/// C#-owned response acknowledging mesh unsubscription.
/// </summary>
public sealed class MeshUnsubscribeResponse
{
    public required string SubscriptionId { get; set; }
}

// ── Mesh Update Event ──

/// <summary>
/// C#-owned event payload for voxelforge.mesh.update.
/// Pushed to subscribed TS clients when the model mesh changes incrementally.
/// </summary>
public sealed class MeshUpdateEventPayload
{
    /// <summary>
    /// The model identifier this update applies to.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// The base mesh snapshot identifier this update is relative to.
    /// TS uses this to verify the update applies to the currently cached mesh.
    /// </summary>
    public required string BaseMeshId { get; init; }

    /// <summary>
    /// The sequence number of this update (monotonically increasing per subscription).
    /// TS can detect gaps and request a full re-sync.
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// Whether this update is incremental (dirty regions only) or a full replacement.
    /// "incremental" or "full_replace".
    /// </summary>
    public required string UpdateType { get; init; }

    /// <summary>
    /// Per-region geometry updates.
    /// </summary>
    public required MeshRegionUpdateDto[] ChangedRegions { get; init; }

    /// <summary>
    /// Payload format: "json".
    /// </summary>
    public required string PayloadFormat { get; init; }

    /// <summary>
    /// Total vertex count in the full model (for TS validation).
    /// </summary>
    public required int FullVertexCount { get; init; }

    /// <summary>
    /// Total index count in the full model.
    /// </summary>
    public required int FullIndexCount { get; init; }

    /// <summary>
    /// Performance metrics for this update.
    /// </summary>
    public MeshUpdateMetricsDto? Metrics { get; init; }
}

/// <summary>
/// A single region's geometry update within a mesh update event.
/// </summary>
public sealed class MeshRegionUpdateDto
{
    /// <summary>
    /// Stable region identifier (e.g., "0_0_0" for region at chunk origin).
    /// </summary>
    public required string RegionId { get; init; }

    /// <summary>
    /// "incremental" or "full_replace".
    /// </summary>
    public required string UpdateKind { get; init; }

    /// <summary>
    /// Spatial bounds of this region.
    /// </summary>
    public required RegionBoundsDto Bounds { get; init; }

    /// <summary>
    /// Vertex offset within the full mesh buffer (0 for per-region buffers).
    /// </summary>
    public required int VertexOffset { get; init; }

    /// <summary>
    /// Number of vertices in this region.
    /// </summary>
    public required int VertexCount { get; init; }

    /// <summary>
    /// Index offset within the full mesh index buffer (0 for per-region buffers).
    /// </summary>
    public required int IndexOffset { get; init; }

    /// <summary>
    /// Number of indices in this region.
    /// </summary>
    public required int IndexCount { get; init; }

    /// <summary>
    /// Flat array of float32 vertex positions for this region.
    /// </summary>
    public required float[] Positions { get; init; }

    /// <summary>
    /// Flat array of float32 vertex normals for this region.
    /// </summary>
    public required float[] Normals { get; init; }

    /// <summary>
    /// Flat array of uint8 RGBA vertex colors for this region.
    /// </summary>
    public required byte[] Colors { get; init; }

    /// <summary>
    /// Per-vertex palette indices for this region. May be null.
    /// </summary>
    public byte[]? PaletteIndices { get; init; }

    /// <summary>
    /// Triangle indices for this region.
    /// </summary>
    public required int[] Indices { get; init; }
}

public sealed class RegionBoundsDto
{
    public required int MinX { get; init; }
    public required int MinY { get; init; }
    public required int MinZ { get; init; }
    public required int MaxX { get; init; }
    public required int MaxY { get; init; }
    public required int MaxZ { get; init; }
}

public sealed class MeshUpdateMetricsDto
{
    public required int RegionCount { get; init; }
    public required long BuildMs { get; init; }
    public required long SerializeMs { get; init; }
}

// ── Palette Update Event ──

/// <summary>
/// C#-owned event payload for voxelforge.palette.update.
/// Pushed when palette entries change.
/// </summary>
public sealed class PaletteUpdateEventPayload
{
    public required string ModelId { get; init; }
    public required long Sequence { get; init; }

    /// <summary>
    /// "full_replace" for initial/complete palette, or "partial" for changed entries only.
    /// </summary>
    public required string UpdateType { get; init; }

    /// <summary>
    /// Changed or full palette entries.
    /// </summary>
    public required PaletteEntryResponse[] Entries { get; init; }

    public required int EntryCount { get; init; }
}

// ── Editor UI State / Commands ──

/// <summary>
/// TS-owned request to subscribe to authoritative editor state updates.
/// </summary>
public sealed class EditorStateSubscribeRequest
{
    public string[] Domains { get; set; } = ["document", "session", "history", "palette", "diagnostics"];
    public string DeliveryMode { get; set; } = "snapshot";
    public bool FullSnapshotOnSubscribe { get; set; } = true;
}

/// <summary>
/// C#-owned response acknowledging editor state subscription.
/// </summary>
public sealed class EditorStateSubscribeResponse
{
    public required string SubscriptionId { get; set; }
    public required string[] Domains { get; set; }
    public required string DeliveryMode { get; set; }
    public EditorUiStateSnapshot? Snapshot { get; set; }
}

/// <summary>
/// TS-owned request for a full authoritative editor state snapshot.
/// </summary>
public sealed class EditorStateRequestFullRequest
{
    public string[] Domains { get; set; } = ["document", "session", "history", "palette", "diagnostics"];
}

/// <summary>
/// C#-owned response with the current authoritative editor state.
/// </summary>
public sealed class EditorStateRequestFullResponse
{
    public required EditorUiStateSnapshot Snapshot { get; set; }
}

/// <summary>
/// C#-owned event payload pushed when UI-relevant editor state changes.
/// </summary>
public sealed class EditorStateDeltaEventPayload
{
    public required string Domain { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool Full { get; init; }
    public required EditorUiStateSnapshot Snapshot { get; init; }
}

/// <summary>
/// Renderer-neutral state snapshot for the Electron tool surface.
/// This deliberately excludes mesh buffers; mesh data travels through mesh messages.
/// </summary>
public sealed class EditorUiStateSnapshot
{
    public required string ModelId { get; init; }
    public string? ProjectPath { get; init; }
    public required bool IsDirty { get; init; }
    public required int VoxelCount { get; init; }
    public BoundsDto? Bounds { get; init; }
    public required int GridHint { get; init; }
    public required string ActiveTool { get; init; }
    public required byte ActivePaletteIndex { get; init; }
    public required string[] AvailableTools { get; init; }
    public required PaletteEntryResponse[] PaletteEntries { get; init; }
    public required int PaletteEntryCount { get; init; }
    public required bool CanUndo { get; init; }
    public required bool CanRedo { get; init; }
    public required int UndoDepth { get; init; }
    public required int RedoDepth { get; init; }
    public string? LastCommandDescription { get; init; }
    public required int SelectedVoxelCount { get; init; }
    public required int ActiveFrameIndex { get; init; }
    public required string StatusMessage { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// TS-owned generic editor command request. C# validates and applies semantics.
/// </summary>
public sealed class CommandExecuteRequest
{
    public required string CommandName { get; set; }
    public Dictionary<string, object?> Arguments { get; set; } = [];
}

/// <summary>
/// C#-owned command result with an authoritative post-command state snapshot.
/// </summary>
public sealed class CommandExecuteResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required string[] AffectedDomains { get; init; }
    public required bool MeshChanged { get; init; }
    public required EditorUiStateSnapshot State { get; init; }
}

public sealed class HistoryUndoRequest
{
}

public sealed class HistoryRedoRequest
{
}

public sealed class HistoryCommandResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required bool MeshChanged { get; init; }
    public required EditorUiStateSnapshot State { get; init; }
}

public sealed class ProjectSaveRequest
{
    public required string Path { get; set; }
}

public sealed class ProjectLoadRequest
{
    public required string Path { get; set; }
}

public sealed class ProjectCommandResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required bool MeshChanged { get; init; }
    public required EditorUiStateSnapshot State { get; init; }
}