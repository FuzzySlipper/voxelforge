using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxelForge.Import;

public sealed class ImportDiagnosticSeverityJsonConverter : JsonConverter<ImportDiagnosticSeverity>
{
    public override ImportDiagnosticSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? value = reader.GetString();
        if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
            return ImportDiagnosticSeverity.Error;
        if (string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase))
            return ImportDiagnosticSeverity.Warning;

        throw new JsonException($"Unknown import diagnostic severity '{value}'.");
    }

    public override void Write(Utf8JsonWriter writer, ImportDiagnosticSeverity value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value == ImportDiagnosticSeverity.Error ? "error" : "warning");
    }
}
