using System.Text.Json.Serialization;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkRunset
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("suite_id")]
    public string? SuiteId { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("artifact_root")]
    public string? ArtifactRoot { get; init; }

    [JsonPropertyName("max_rounds")]
    public int? MaxRounds { get; init; }

    [JsonPropertyName("trials")]
    public int? Trials { get; init; }

    [JsonPropertyName("tool_preset")]
    public string? ToolPreset { get; init; }

    [JsonPropertyName("cases")]
    public List<BenchmarkCase>? Cases { get; init; }

    [JsonPropertyName("variants")]
    public List<BenchmarkVariant>? Variants { get; init; }
}

public sealed class BenchmarkCase
{
    [JsonPropertyName("case_id")]
    public string? CaseId { get; init; }

    [JsonPropertyName("prompt_file")]
    public string? PromptFile { get; init; }

    [JsonPropertyName("system_prompt_file")]
    public string? SystemPromptFile { get; init; }

    [JsonPropertyName("initial_model")]
    public string? InitialModel { get; init; }

    [JsonPropertyName("palette_file")]
    public string? PaletteFile { get; init; }

    [JsonPropertyName("expected_tags")]
    public List<string>? ExpectedTags { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

public sealed class BenchmarkVariant
{
    [JsonPropertyName("variant_id")]
    public string? VariantId { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("seed")]
    public int? Seed { get; init; }

    [JsonPropertyName("system_prompt_override")]
    public string? SystemPromptOverride { get; init; }

    [JsonPropertyName("tool_preset")]
    public string? ToolPreset { get; init; }

    [JsonPropertyName("environment")]
    public string? Environment { get; init; }
}
