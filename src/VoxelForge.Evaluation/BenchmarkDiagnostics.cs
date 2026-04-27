namespace VoxelForge.Evaluation;

public enum BenchmarkDiagnosticSeverity
{
    Error,
    Warning,
}

public sealed class BenchmarkDiagnostic
{
    public required BenchmarkDiagnosticSeverity Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Location { get; init; }
}

public sealed class BenchmarkValidationResult
{
    public required IReadOnlyList<BenchmarkDiagnostic> Diagnostics { get; init; }

    public bool Success
    {
        get
        {
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity == BenchmarkDiagnosticSeverity.Error)
                    return false;
            }

            return true;
        }
    }
}
