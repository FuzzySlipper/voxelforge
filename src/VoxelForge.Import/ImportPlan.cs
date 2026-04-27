using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.Import;

public enum ImportInputFormat
{
    Auto,
    ToolEnvelope,
    RawArguments,
    ToolCallArray,
    ToolCallsJsonl,
    StdioJsonl,
}

public sealed class VoxelForgeImportPlan
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("source")]
    public required ImportPlanSource Source { get; init; }

    [JsonPropertyName("options")]
    public required ImportPlanOptions Options { get; init; }

    [JsonPropertyName("operations")]
    public required IReadOnlyList<ImportPlanOperation> Operations { get; init; }
}

public sealed class ImportPlanSource
{
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("captured_at_utc")]
    public required string CapturedAtUtc { get; init; }
}

public sealed class ImportPlanOptions
{
    [JsonPropertyName("strict")]
    public required bool Strict { get; init; }

    [JsonPropertyName("max_operations")]
    public required int MaxOperations { get; init; }

    [JsonPropertyName("max_generated_voxels")]
    public required int MaxGeneratedVoxels { get; init; }
}

public sealed class ImportPlanOperation
{
    [JsonPropertyName("operation_id")]
    public required string OperationId { get; init; }

    [JsonPropertyName("source_index")]
    public required int SourceIndex { get; init; }

    [JsonPropertyName("source_line")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourceLine { get; init; }

    [JsonPropertyName("source_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceCallId { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public required JsonElement Arguments { get; init; }

    [JsonPropertyName("effect")]
    public required string Effect { get; init; }
}

public sealed class ImportNormalizeOptions
{
    public ImportInputFormat Format { get; init; } = ImportInputFormat.Auto;
    public string? ToolName { get; init; }
    public bool Strict { get; init; } = true;
    public int MaxOperations { get; init; } = 10000;
    public int MaxGeneratedVoxels { get; init; } = 65536;
    public DateTimeOffset? CapturedAtUtc { get; init; }
}

public sealed class ImportNormalizeResult
{
    public required bool Success { get; init; }
    public VoxelForgeImportPlan? Plan { get; init; }
    public required ImportReport Report { get; init; }
    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }
}
