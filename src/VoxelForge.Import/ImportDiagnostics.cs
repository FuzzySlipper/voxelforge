using System.Text.Json.Serialization;

namespace VoxelForge.Import;

[JsonConverter(typeof(ImportDiagnosticSeverityJsonConverter))]
public enum ImportDiagnosticSeverity
{
    Error,
    Warning,
}

public sealed class ImportDiagnostic
{
    [JsonPropertyName("severity")]
    public required ImportDiagnosticSeverity Severity { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("source_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePath { get; init; }

    [JsonPropertyName("line")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Line { get; init; }

    [JsonPropertyName("column")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Column { get; init; }

    [JsonPropertyName("operation_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OperationIndex { get; init; }

    [JsonPropertyName("tool_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; init; }

    [JsonPropertyName("source_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceCallId { get; init; }

    [JsonPropertyName("json_pointer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JsonPointer { get; init; }
}

public sealed class ImportReportOperation
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

    [JsonPropertyName("effect")]
    public required string Effect { get; init; }
}

public sealed class ImportReport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("source_format")]
    public required string SourceFormat { get; init; }

    [JsonPropertyName("operation_count")]
    public required int OperationCount { get; init; }

    [JsonPropertyName("accepted_operation_count")]
    public required int AcceptedOperationCount { get; init; }

    [JsonPropertyName("skipped_read_only_count")]
    public required int SkippedReadOnlyCount { get; init; }

    [JsonPropertyName("error_count")]
    public required int ErrorCount { get; init; }

    [JsonPropertyName("warning_count")]
    public required int WarningCount { get; init; }

    [JsonPropertyName("operations")]
    public IReadOnlyList<ImportReportOperation> Operations { get; init; } = [];

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; }
}
