using System.Text.Json.Serialization;

namespace VoxelForge.Core.Reference;

/// <summary>
/// Parsed data from a Unity .mat YAML sidecar file.
/// Contains texture slots, color properties, and scalar fields
/// that approximate the material's appearance before voxelization.
/// </summary>
public sealed class UnityMatData
{
    /// <summary>Material name from m_Name field.</summary>
    public string? MaterialName { get; set; }

    // --- Texture slots ---

    /// <summary>_MainTex or _BaseMap texture reference (GUID or path).</summary>
    public UnityTextureRef? MainTex { get; set; }

    /// <summary>_BaseColorMap texture reference (HDRP/Lit).</summary>
    public UnityTextureRef? BaseColorMap { get; set; }

    /// <summary>_EmissionMap texture reference.</summary>
    public UnityTextureRef? EmissionMap { get; set; }

    // --- Color properties ---

    /// <summary>_Color or _BaseColor RGBA (0-1 range). Null if absent.</summary>
    public UnityVector4? MainColor { get; set; }

    /// <summary>_EmissionColor RGBA (0-1 range, may exceed 1). Null if absent.</summary>
    public UnityVector4? EmissionColor { get; set; }

    // --- Scalar properties ---

    public float? Cutoff { get; set; }
    public float? Glossiness { get; set; }
    public float? Metallic { get; set; }

    // --- Diagnostic metadata ---

    /// <summary>Full path to the .mat file from which this was parsed.</summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>Property names found but not extracted (unsupported shader properties).</summary>
    public List<string> IgnoredProperties { get; set; } = [];

    /// <summary>Resolved texture paths after GUID resolution.</summary>
    public Dictionary<string, string> ResolvedTextures { get; set; } = new();

    /// <summary>GUIDs that could not be resolved through .meta files.</summary>
    public List<string> UnresolvedGuids { get; set; } = [];
}

/// <summary>
/// A Unity texture reference from a .mat file — either a GUID or a direct path.
/// </summary>
public sealed class UnityTextureRef
{
    /// <summary>GUID string from m_Texture block, or null if path-based.</summary>
    public string? Guid { get; set; }

    /// <summary>Direct fileID if known (not used for resolution).</summary>
    public long? FileId { get; set; }

    /// <summary>Direct path hint, if available.</summary>
    public string? PathHint { get; set; }

    /// <summary>Unity asset type code (e.g., 3 for Texture2D).</summary>
    public int? Type { get; set; }
}

/// <summary>
/// A Unity RGBA vector value (0-1 range, but can exceed 1 for emission).
/// </summary>
public readonly record struct UnityVector4(float R, float G, float B, float A);

/// <summary>
/// Result of attempting to match and apply a Unity .mat sidecar to a model material.
/// </summary>
public sealed class UnityMatMatchResult
{
    /// <summary>The matched .mat file path (full path).</summary>
    public required string MatFilePath { get; set; }

    /// <summary>The model material name that was matched.</summary>
    public required string MatchedMaterialName { get; set; }

    /// <summary>Parsed Unity material data.</summary>
    public UnityMatData? ParsedData { get; set; }

    /// <summary>How this match was determined.</summary>
    public required UnityMatMatchKind MatchKind { get; set; }

    /// <summary>Warnings from the matching process.</summary>
    public List<string> Warnings { get; set; } = [];
}

public enum UnityMatMatchKind
{
    /// <summary>Exact name match between .mat m_Name and model material name.</summary>
    ExactName,

    /// <summary>Filename stem match (e.g., MyModel.mat for model material "MyModel").</summary>
    FilenameStem,

    /// <summary>Ambiguous — multiple candidates matched; heuristic chosen.</summary>
    Ambiguous,

    /// <summary>No .mat found; system default fallback.</summary>
    None,
}

/// <summary>
/// Aggregate result of Unity .mat sidecar processing for an entire model.
/// </summary>
public sealed class UnityMatSidecarResult
{
    /// <summary>Per-material match results.</summary>
    public List<UnityMatMatchResult> Matches { get; set; } = [];

    /// <summary>Warning messages about the overall sidecar processing.</summary>
    public List<string> GlobalWarnings { get; set; } = [];

    /// <summary>Whether any .mat files were found at all.</summary>
    public bool FoundAnyMatFiles { get; set; }
}
