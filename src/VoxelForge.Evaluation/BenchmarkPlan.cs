namespace VoxelForge.Evaluation;

public sealed class BenchmarkPlanOptions
{
    public string? CaseId { get; init; }
    public string? VariantId { get; init; }
    public int? TrialsOverride { get; init; }
    public string? ArtifactRootOverride { get; init; }
    public string Backend { get; init; } = BenchmarkPlanner.DefaultBackend;
    public bool FailFast { get; init; }
}

public sealed class BenchmarkPlanResult
{
    public required bool Success { get; init; }
    public BenchmarkRunPlan? Plan { get; init; }
    public required IReadOnlyList<BenchmarkDiagnostic> Diagnostics { get; init; }
}

public sealed class BenchmarkRunPlan
{
    public required string SuiteId { get; init; }
    public required string ArtifactRoot { get; init; }
    public required string Backend { get; init; }
    public required bool FailFast { get; init; }
    public int? MaxRounds { get; init; }
    public required IReadOnlyList<BenchmarkPlannedRun> Runs { get; init; }
}

public sealed class BenchmarkPlannedRun
{
    public required string CaseId { get; init; }
    public required string VariantId { get; init; }
    public required int Trial { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public required string PromptFile { get; init; }
    public string? SystemPromptFile { get; init; }
    public string? InitialModel { get; init; }
    public string? PaletteFile { get; init; }
    public string? ToolPreset { get; init; }
    public double? Temperature { get; init; }
    public int? Seed { get; init; }
}

public sealed class BenchmarkPlanner
{
    public const string DefaultBackend = "mcp-tool-loop";

    private static readonly string[] SupportedBackends = ["mcp-tool-loop", "stdio"];
    private readonly BenchmarkRunsetValidator _validator;

    public BenchmarkPlanner(BenchmarkRunsetValidator validator)
    {
        _validator = validator;
    }

    public BenchmarkPlanResult BuildPlan(BenchmarkRunset runset, BenchmarkPlanOptions options)
    {
        ArgumentNullException.ThrowIfNull(runset);
        ArgumentNullException.ThrowIfNull(options);

        var diagnostics = new List<BenchmarkDiagnostic>();
        BenchmarkValidationResult validation = _validator.Validate(runset);
        diagnostics.AddRange(validation.Diagnostics);

        if (!IsSupportedBackend(options.Backend))
        {
            AddError(
                diagnostics,
                "plan.backend.unsupported",
                $"Unsupported backend '{options.Backend}'. Expected mcp-tool-loop or stdio.",
                "--backend");
        }

        if (options.TrialsOverride.HasValue && options.TrialsOverride.Value < 1)
            AddError(diagnostics, "plan.trials", "--trials must be at least 1.", "--trials");

        if (!string.IsNullOrWhiteSpace(options.ArtifactRootOverride))
        {
            BenchmarkRunset overrideRunset = new()
            {
                SchemaVersion = 1,
                SuiteId = "override-check",
                ArtifactRoot = options.ArtifactRootOverride,
                Cases = [new BenchmarkCase { CaseId = "case", PromptFile = "prompt.md" }],
                Variants = [new BenchmarkVariant { VariantId = "variant", Provider = "provider", Model = "model" }],
            };
            BenchmarkValidationResult overrideValidation = _validator.Validate(overrideRunset);
            for (int i = 0; i < overrideValidation.Diagnostics.Count; i++)
            {
                BenchmarkDiagnostic diagnostic = overrideValidation.Diagnostics[i];
                if (diagnostic.Code.StartsWith("runset.artifact_root", StringComparison.Ordinal))
                    diagnostics.Add(diagnostic);
            }
        }

        IReadOnlyList<BenchmarkCase> cases = runset.Cases ?? [];
        IReadOnlyList<BenchmarkVariant> variants = runset.Variants ?? [];

        if (!string.IsNullOrWhiteSpace(options.CaseId))
        {
            cases = FilterCases(cases, options.CaseId);
            if (cases.Count == 0)
                AddError(diagnostics, "plan.case_filter.not_found", $"No case matches --case {options.CaseId}.", "--case");
        }

        if (!string.IsNullOrWhiteSpace(options.VariantId))
        {
            variants = FilterVariants(variants, options.VariantId);
            if (variants.Count == 0)
                AddError(diagnostics, "plan.variant_filter.not_found", $"No variant matches --variant {options.VariantId}.", "--variant");
        }

        if (diagnostics.Any(diagnostic => diagnostic.Severity == BenchmarkDiagnosticSeverity.Error))
            return Failure(diagnostics);

        int trials = options.TrialsOverride ?? runset.Trials ?? 1;
        var runs = new List<BenchmarkPlannedRun>(cases.Count * variants.Count * trials);
        for (int caseIndex = 0; caseIndex < cases.Count; caseIndex++)
        {
            BenchmarkCase benchmarkCase = cases[caseIndex];
            for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
            {
                BenchmarkVariant variant = variants[variantIndex];
                for (int trial = 1; trial <= trials; trial++)
                {
                    runs.Add(new BenchmarkPlannedRun
                    {
                        CaseId = benchmarkCase.CaseId!,
                        VariantId = variant.VariantId!,
                        Trial = trial,
                        Provider = variant.Provider!,
                        Model = variant.Model!,
                        PromptFile = benchmarkCase.PromptFile!,
                        SystemPromptFile = variant.SystemPromptOverride ?? benchmarkCase.SystemPromptFile,
                        InitialModel = benchmarkCase.InitialModel,
                        PaletteFile = benchmarkCase.PaletteFile,
                        ToolPreset = variant.ToolPreset ?? runset.ToolPreset,
                        Temperature = variant.Temperature,
                        Seed = variant.Seed,
                    });
                }
            }
        }

        return new BenchmarkPlanResult
        {
            Success = true,
            Diagnostics = diagnostics,
            Plan = new BenchmarkRunPlan
            {
                SuiteId = runset.SuiteId!,
                ArtifactRoot = options.ArtifactRootOverride ?? runset.ArtifactRoot!,
                Backend = options.Backend,
                FailFast = options.FailFast,
                MaxRounds = runset.MaxRounds,
                Runs = runs,
            },
        };
    }

    private static bool IsSupportedBackend(string backend)
    {
        for (int i = 0; i < SupportedBackends.Length; i++)
        {
            if (string.Equals(backend, SupportedBackends[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static BenchmarkCase[] FilterCases(IReadOnlyList<BenchmarkCase> cases, string caseId)
    {
        var filtered = new List<BenchmarkCase>();
        for (int i = 0; i < cases.Count; i++)
        {
            if (string.Equals(cases[i].CaseId, caseId, StringComparison.Ordinal))
                filtered.Add(cases[i]);
        }

        return filtered.ToArray();
    }

    private static BenchmarkVariant[] FilterVariants(IReadOnlyList<BenchmarkVariant> variants, string variantId)
    {
        var filtered = new List<BenchmarkVariant>();
        for (int i = 0; i < variants.Count; i++)
        {
            if (string.Equals(variants[i].VariantId, variantId, StringComparison.Ordinal))
                filtered.Add(variants[i]);
        }

        return filtered.ToArray();
    }

    private static BenchmarkPlanResult Failure(List<BenchmarkDiagnostic> diagnostics)
    {
        return new BenchmarkPlanResult
        {
            Success = false,
            Diagnostics = diagnostics,
        };
    }

    private static void AddError(List<BenchmarkDiagnostic> diagnostics, string code, string message, string location)
    {
        diagnostics.Add(new BenchmarkDiagnostic
        {
            Severity = BenchmarkDiagnosticSeverity.Error,
            Code = code,
            Message = message,
            Location = location,
        });
    }
}
