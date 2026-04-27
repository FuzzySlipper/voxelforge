namespace VoxelForge.Evaluation;

public sealed class BenchmarkPlanWriter
{
    public void Write(BenchmarkRunPlan plan, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine($"Suite: {plan.SuiteId}");
        writer.WriteLine($"Artifact root: {plan.ArtifactRoot}");
        writer.WriteLine($"Backend: {plan.Backend}");
        writer.WriteLine($"Fail fast: {plan.FailFast.ToString().ToLowerInvariant()}");
        writer.WriteLine($"Runs: {plan.Runs.Count}");

        for (int i = 0; i < plan.Runs.Count; i++)
        {
            BenchmarkPlannedRun run = plan.Runs[i];
            writer.Write("- ");
            writer.Write(run.CaseId);
            writer.Write(" | ");
            writer.Write(run.VariantId);
            writer.Write(" | trial ");
            writer.Write(run.Trial);
            writer.Write(" | provider ");
            writer.Write(run.Provider);
            writer.Write(" | model ");
            writer.Write(run.Model);
            writer.Write(" | prompt ");
            writer.Write(run.PromptFile);
            if (!string.IsNullOrWhiteSpace(run.ToolPreset))
            {
                writer.Write(" | tool_preset ");
                writer.Write(run.ToolPreset);
            }

            writer.WriteLine();
        }
    }
}
