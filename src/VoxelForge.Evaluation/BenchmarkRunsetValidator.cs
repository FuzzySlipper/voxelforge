namespace VoxelForge.Evaluation;

public sealed class BenchmarkRunsetValidator
{
    private const int SupportedSchemaVersion = 1;

    public BenchmarkValidationResult Validate(BenchmarkRunset? runset)
    {
        var diagnostics = new List<BenchmarkDiagnostic>();
        if (runset is null)
        {
            AddError(diagnostics, "runset.required", "Runset JSON must contain an object.", "$.");
            return ToResult(diagnostics);
        }

        if (runset.SchemaVersion != SupportedSchemaVersion)
        {
            AddError(
                diagnostics,
                "runset.schema_version",
                $"schema_version must be {SupportedSchemaVersion}.",
                "$.schema_version");
        }

        ValidateIdentifier(diagnostics, runset.SuiteId, "runset.suite_id", "suite_id", required: true);
        ValidateSafePath(diagnostics, runset.ArtifactRoot, "runset.artifact_root", "artifact_root", required: true, expectedExtension: null);

        if (runset.MaxRounds.HasValue && runset.MaxRounds.Value < 1)
            AddError(diagnostics, "runset.max_rounds", "max_rounds must be at least 1.", "$.max_rounds");

        if (runset.Trials.HasValue && runset.Trials.Value < 1)
            AddError(diagnostics, "runset.trials", "trials must be at least 1.", "$.trials");

        ValidateOptionalIdentifier(diagnostics, runset.ToolPreset, "runset.tool_preset", "tool_preset");

        if (runset.Cases is null || runset.Cases.Count == 0)
        {
            AddError(diagnostics, "runset.cases.required", "cases must contain at least one case.", "$.cases");
        }
        else
        {
            ValidateCases(diagnostics, runset.Cases);
        }

        if (runset.Variants is null || runset.Variants.Count == 0)
        {
            AddError(diagnostics, "runset.variants.required", "variants must contain at least one variant.", "$.variants");
        }
        else
        {
            ValidateVariants(diagnostics, runset.Variants);
        }

        return ToResult(diagnostics);
    }

    private static void ValidateCases(List<BenchmarkDiagnostic> diagnostics, IReadOnlyList<BenchmarkCase> cases)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < cases.Count; i++)
        {
            BenchmarkCase benchmarkCase = cases[i];
            string location = $"$.cases[{i}]";
            ValidateIdentifier(diagnostics, benchmarkCase.CaseId, "case.case_id", "case_id", required: true, location);
            if (!string.IsNullOrWhiteSpace(benchmarkCase.CaseId) && !seenIds.Add(benchmarkCase.CaseId))
                AddError(diagnostics, "case.case_id.duplicate", $"Duplicate case_id '{benchmarkCase.CaseId}'.", location + ".case_id");

            ValidateSafePath(diagnostics, benchmarkCase.PromptFile, "case.prompt_file", "prompt_file", required: true, expectedExtension: null, location);
            ValidateSafePath(diagnostics, benchmarkCase.SystemPromptFile, "case.system_prompt_file", "system_prompt_file", required: false, expectedExtension: null, location);
            ValidateSafePath(diagnostics, benchmarkCase.InitialModel, "case.initial_model", "initial_model", required: false, expectedExtension: ".vforge", location);
            ValidateSafePath(diagnostics, benchmarkCase.PaletteFile, "case.palette_file", "palette_file", required: false, expectedExtension: ".json", location);
        }
    }

    private static void ValidateVariants(List<BenchmarkDiagnostic> diagnostics, IReadOnlyList<BenchmarkVariant> variants)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < variants.Count; i++)
        {
            BenchmarkVariant variant = variants[i];
            string location = $"$.variants[{i}]";
            ValidateIdentifier(diagnostics, variant.VariantId, "variant.variant_id", "variant_id", required: true, location);
            if (!string.IsNullOrWhiteSpace(variant.VariantId) && !seenIds.Add(variant.VariantId))
                AddError(diagnostics, "variant.variant_id.duplicate", $"Duplicate variant_id '{variant.VariantId}'.", location + ".variant_id");

            ValidateRequiredText(diagnostics, variant.Provider, "variant.provider", "provider", location);
            ValidateRequiredText(diagnostics, variant.Model, "variant.model", "model", location);
            ValidateOptionalIdentifier(diagnostics, variant.ToolPreset, "variant.tool_preset", "tool_preset", location);
            ValidateOptionalIdentifier(diagnostics, variant.Environment, "variant.environment", "environment", location);
            ValidateSafePath(diagnostics, variant.SystemPromptOverride, "variant.system_prompt_override", "system_prompt_override", required: false, expectedExtension: null, location);

            if (variant.Temperature.HasValue && variant.Temperature.Value < 0)
                AddError(diagnostics, "variant.temperature", "temperature must be non-negative.", location + ".temperature");

            if (variant.TopP.HasValue && (variant.TopP.Value <= 0 || variant.TopP.Value > 1))
                AddError(diagnostics, "variant.top_p", "top_p must be greater than 0 and at most 1.", location + ".top_p");

            if (variant.MaxTokens.HasValue && variant.MaxTokens.Value < 1)
                AddError(diagnostics, "variant.max_tokens", "max_tokens must be at least 1.", location + ".max_tokens");
        }
    }

    private static void ValidateRequiredText(
        List<BenchmarkDiagnostic> diagnostics,
        string? value,
        string code,
        string fieldName,
        string location)
    {
        if (string.IsNullOrWhiteSpace(value))
            AddError(diagnostics, code, $"{fieldName} is required.", location + "." + fieldName);
    }

    private static void ValidateIdentifier(
        List<BenchmarkDiagnostic> diagnostics,
        string? value,
        string code,
        string fieldName,
        bool required,
        string locationPrefix = "$")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                AddError(diagnostics, code + ".required", $"{fieldName} is required.", locationPrefix + "." + fieldName);

            return;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
            if (!valid)
            {
                AddError(
                    diagnostics,
                    code + ".unsafe",
                    $"{fieldName} must be a stable directory-safe identifier using only letters, digits, '.', '_' or '-'.",
                    locationPrefix + "." + fieldName);
                return;
            }
        }
    }

    private static void ValidateOptionalIdentifier(
        List<BenchmarkDiagnostic> diagnostics,
        string? value,
        string code,
        string fieldName,
        string locationPrefix = "$")
    {
        ValidateIdentifier(diagnostics, value, code, fieldName, required: false, locationPrefix);
    }

    private static void ValidateSafePath(
        List<BenchmarkDiagnostic> diagnostics,
        string? value,
        string code,
        string fieldName,
        bool required,
        string? expectedExtension,
        string locationPrefix = "$")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
                AddError(diagnostics, code + ".required", $"{fieldName} is required.", locationPrefix + "." + fieldName);

            return;
        }

        if (Path.IsPathRooted(value))
        {
            AddError(diagnostics, code + ".rooted", $"{fieldName} must be a relative path.", locationPrefix + "." + fieldName);
            return;
        }

        string normalized = value.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            AddError(diagnostics, code + ".empty", $"{fieldName} must not be empty.", locationPrefix + "." + fieldName);
            return;
        }

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == "..")
            {
                AddError(diagnostics, code + ".traversal", $"{fieldName} must not contain '..' path traversal.", locationPrefix + "." + fieldName);
                return;
            }
        }

        if (expectedExtension is not null && !value.EndsWith(expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddError(diagnostics, code + ".extension", $"{fieldName} must end with {expectedExtension}.", locationPrefix + "." + fieldName);
        }
    }

    private static BenchmarkValidationResult ToResult(List<BenchmarkDiagnostic> diagnostics)
    {
        return new BenchmarkValidationResult
        {
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
