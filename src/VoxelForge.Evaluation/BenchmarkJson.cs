using System.Text.Json;

namespace VoxelForge.Evaluation;

internal static class BenchmarkJson
{
    public static JsonSerializerOptions WriteIndentedOptions { get; } = new()
    {
        WriteIndented = true,
    };

    public static JsonSerializerOptions ReadOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static JsonSerializerOptions JsonlOptions { get; } = new()
    {
        WriteIndented = false,
    };
}
