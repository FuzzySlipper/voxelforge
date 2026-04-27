using System.Text.Json.Serialization;

namespace VoxelForge.App.Console;

/// <summary>
/// JSON-line request shape shared by StdioHost and headless stdio-compatible adapters.
/// </summary>
public sealed class StdioCommandRequest
{
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("args")]
    public string[]? Args { get; init; }
}

/// <summary>
/// JSON-line response shape shared by StdioHost and headless stdio-compatible adapters.
/// </summary>
public sealed class StdioCommandResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Image { get; set; }

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Images { get; set; }
}
