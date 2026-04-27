using System.Text.Json;

namespace VoxelForge.Evaluation;

public sealed class BenchmarkRunsetLoadResult
{
    public required bool Success { get; init; }
    public BenchmarkRunset? Runset { get; init; }
    public required IReadOnlyList<BenchmarkDiagnostic> Diagnostics { get; init; }
}

public sealed class BenchmarkRunsetLoader
{
    private readonly BenchmarkRunsetValidator _validator;
    private readonly JsonSerializerOptions _jsonOptions;

    public BenchmarkRunsetLoader(BenchmarkRunsetValidator validator)
    {
        _validator = validator;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
    }

    public BenchmarkRunsetLoadResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return Failure(new BenchmarkDiagnostic
            {
                Severity = BenchmarkDiagnosticSeverity.Error,
                Code = "runset.file_not_found",
                Message = $"Runset file not found: {path}",
                Location = path,
            });
        }

        BenchmarkRunset? runset;
        try
        {
            string json = File.ReadAllText(path);
            runset = JsonSerializer.Deserialize<BenchmarkRunset>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return Failure(new BenchmarkDiagnostic
            {
                Severity = BenchmarkDiagnosticSeverity.Error,
                Code = "runset.invalid_json",
                Message = ex.Message,
                Location = path,
            });
        }

        BenchmarkValidationResult validation = _validator.Validate(runset);
        return new BenchmarkRunsetLoadResult
        {
            Success = validation.Success,
            Runset = runset,
            Diagnostics = validation.Diagnostics,
        };
    }

    private static BenchmarkRunsetLoadResult Failure(BenchmarkDiagnostic diagnostic)
    {
        return new BenchmarkRunsetLoadResult
        {
            Success = false,
            Diagnostics = [diagnostic],
        };
    }
}
