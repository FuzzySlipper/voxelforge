# LLM Headless Benchmark Harness

Task 753 defines a repeatable local evaluation workflow for VoxelForge LLM authoring runs. The goal is to make prompt, model, and tool-surface changes comparable without coupling evaluation code to the FNA/Myra editor or inventing a separate mutation path.

This is a design document. It fixes the first benchmark workflow, artifact layout, comparison scope, and implementation split so follow-up tasks can build the harness without re-deciding the API.

## Goals

- Run the same voxel-authoring prompt across model/provider variants and prompt/tool variants.
- Capture enough artifacts to reproduce and inspect each run.
- Compare outputs in a way that is useful before any renderer-driven screenshots exist.
- Reuse VoxelForge's headless, stdio, MCP, Core/App/LLM, and undo-aware seams.
- Keep benchmark orchestration local-first rather than building public leaderboard infrastructure.
- Keep evaluation free of Engine, FNA, Myra, screenshot-provider, and windowing dependencies.

## Non-goals

- No public leaderboard, remote job service, or account management in the first version.
- No renderer or screenshot dependency in the first version.
- No model-provider-specific scoring logic in Core or App.
- No benchmark-only voxel mutation APIs.
- No automatic human-quality score. Human review may annotate artifacts, but the harness should first produce deterministic run data and simple metrics.

## Terminology

- **Suite**: A named collection of related benchmark cases, such as `primitive-builds` or `region-labeling`.
- **Case**: One prompt plus optional initial model and expected metadata.
- **Variant**: One model/provider/prompt/tool configuration applied to each case.
- **Run**: One execution of one case under one variant.
- **Trial**: A repeated run of the same case and variant. Trials are useful when a model is nondeterministic.
- **Artifact root**: The directory containing immutable run outputs and comparison reports.
- **Transcript**: Ordered record of prompts, model responses, tool calls, tool results, command requests, errors, and final answer text.

These names avoid overloading `Context`. Persisted benchmark data should use `Manifest`, `Snapshot`, `Transcript`, `Metrics`, or `Report` names rather than `Context`, following the ESS naming guidance.

## First-version scope decision

The first implementation should compare a combination of:

1. Final `.vforge` outputs.
2. Structured model metrics derived from Core/App state.
3. Command/tool transcripts.

Screenshots are explicitly not part of the required first version.

Rationale:

- `.vforge` is the durable artifact the editor already owns.
- Metrics and hashes let runs be compared in CI or headless local environments.
- Transcripts explain why two outputs differ and show whether the model used the intended tool surface.
- Screenshots require the FNA renderer and are unavailable in `VoxelForge.Mcp` headless mode. They can be added later as an optional post-process that launches the Engine project, but benchmark correctness must not depend on them.

## Architecture overview

The benchmark harness is a local adapter over existing state and tool surfaces:

```text
Benchmark runset
  -> benchmark orchestrator
  -> isolated headless VoxelForge session
  -> model/tool execution loop
  -> App/Core services and undoable commands
  -> save final .vforge + transcript + metrics
  -> comparison report
```

The harness must not mutate `VoxelModel`, `LabelIndex`, palettes, or files directly except through existing service/persistence seams. It may launch an existing headless process or compose the same App/Core services in process. In both cases, VoxelForge state changes still flow through typed services, undoable commands, and existing tool adapters.

Recommended project shape for the first implementation:

```text
src/VoxelForge.Evaluation
  VoxelForge.Evaluation.csproj
  Program.cs
  BenchmarkRunset.cs
  BenchmarkRunManifest.cs
  BenchmarkRunner.cs
  BenchmarkArtifactWriter.cs
  BenchmarkMetricsService.cs
  BenchmarkComparisonService.cs
```

Dependency direction:

```text
VoxelForge.Evaluation -> { VoxelForge.App, VoxelForge.Core, VoxelForge.LLM, VoxelForge.Mcp? }
```

`VoxelForge.Evaluation` must not reference `VoxelForge.Engine.MonoGame`, FNA, Myra, or screenshot code. If the implementation uses the MCP server out-of-process, it can avoid a project reference to `VoxelForge.Mcp` and communicate through the MCP endpoint. If it composes MCP tools in process, the reference remains headless and still respects the existing `Mcp -> App -> Core` seam.

## Execution backends

The harness should support a small backend abstraction so it can start with one backend while preserving the right extension point.

```csharp
public interface IBenchmarkExecutionBackend
{
    Task<BenchmarkRunResult> RunAsync(BenchmarkRunRequest request, CancellationToken ct);
}
```

Named implementation classes are required. Do not use lambdas or reflection registration.

### Backend 1: MCP tool-loop backend (preferred v1 path)

The preferred first backend is a headless MCP/tool-loop execution path.

Responsibilities:

- Create an isolated project directory for the run.
- Start or compose the headless MCP session with that project directory.
- Reset state with `new_model` or load the case's initial `.vforge`.
- Expose the registered MCP tools to the selected model/client.
- Run the prompt until final model response or max rounds.
- Save the final model with `save_model`.
- Capture the full tool-call transcript.
- Query `get_model_info` and other read-only tools for metrics.

Why MCP first:

- It is already a headless adapter over App/Core state.
- It includes model lifecycle, palette, region, spatial query, and voxel editing tools.
- It makes the evaluated tool surface close to what external agents will use.
- Visual tools already return clear headless limitations, matching the benchmark non-goal.

The MCP backend should not rebuild console command strings to trigger behavior. It should call first-class MCP tools or typed App/Core services.

### Backend 2: stdio command backend (compatibility path)

A stdio backend may be added for agents that already emit VoxelForge JSON-line console commands.

Responsibilities:

- Launch `dotnet run --project src/VoxelForge.Engine.MonoGame -- --headless` with stdin/stdout redirected, or launch a future App-only stdio composition root if one exists.
- Send one JSON command per line using the existing stdio request shape:
  - `{"command":"set","args":["0","0","0","1"]}`
- Capture every request/response pair.
- Save the final `.vforge` through the `save` console command.

This backend is useful for command-trace replay, but it is not the primary tool surface for new LLM features. New benchmark cases that need modern LLM tools should prefer MCP or direct ToolLoop execution.

### Backend 3: in-process ToolLoop backend (optional later)

A later backend can compose `ToolLoop`, `ICompletionService`, handlers, `LlmToolApplicationService`, and App state in process.

Use this when the benchmark needs very fine-grained Core transcript data or fake-completion deterministic tests. It should still apply `ToolLoopResult.MutationIntents` through `LlmToolApplicationService.ApplyMutationIntents`, not by mutating the model directly.

## Runset file

The harness reads a local JSON runset file. JSON is chosen over YAML to avoid adding a parser dependency in the first implementation.

Example:

```json
{
  "schema_version": 1,
  "suite_id": "primitive-builds",
  "description": "Small voxel authoring prompts for high-level primitive tools.",
  "artifact_root": "artifacts/benchmarks",
  "max_rounds": 12,
  "trials": 1,
  "tool_preset": "mcp-authoring-v1",
  "cases": [
    {
      "case_id": "simple-chair",
      "prompt_file": "benchmarks/prompts/simple-chair.md",
      "system_prompt_file": "benchmarks/prompts/voxel-authoring-system.md",
      "initial_model": null,
      "palette_file": "benchmarks/palettes/basic-materials.json",
      "expected_tags": ["furniture", "symmetry"],
      "notes": "Should produce a seat, back, and four legs."
    }
  ],
  "variants": [
    {
      "variant_id": "baseline-tools",
      "provider": "configured-chat-client",
      "model": "local-model-name-or-provider-model-id",
      "temperature": 0.2,
      "system_prompt_override": null,
      "tool_preset": "mcp-authoring-v1"
    },
    {
      "variant_id": "primitive-tools",
      "provider": "configured-chat-client",
      "model": "local-model-name-or-provider-model-id",
      "temperature": 0.2,
      "system_prompt_override": "benchmarks/prompts/voxel-authoring-with-primitives.md",
      "tool_preset": "mcp-authoring-with-primitives-v1"
    }
  ]
}
```

### Required runset fields

- `schema_version`: integer, initially `1`.
- `suite_id`: stable directory-safe identifier.
- `artifact_root`: output directory for immutable run artifacts.
- `cases`: non-empty array.
- `variants`: non-empty array.

### Required case fields

- `case_id`: stable directory-safe identifier.
- `prompt_file`: user prompt text.

### Optional case fields

- `system_prompt_file`: case-specific system prompt. If omitted, the variant/default system prompt is used.
- `initial_model`: `.vforge` file copied into the isolated run directory before execution.
- `palette_file`: JSON palette setup applied through model lifecycle/palette tools.
- `expected_tags`: descriptive metadata for filtering reports.
- `notes`: human-readable notes for reviewers.

### Required variant fields

- `variant_id`: stable directory-safe identifier.
- `provider`: logical provider name, not a secret.
- `model`: model identifier as configured locally.

### Optional variant fields

- `temperature`, `top_p`, `max_tokens`, and `seed` when supported by the configured provider.
- `system_prompt_override` to compare prompt variants.
- `tool_preset` to compare tool availability, such as baseline precise tools versus primitive tools.
- `environment` for local provider endpoint labels such as `ollama-local` or `openai-compatible-dev`, without secrets.

## Artifact layout

Each run writes an immutable directory:

```text
artifacts/benchmarks/<suite-id>/<timestamp-utc>/<case-id>/<variant-id>/trial-<n>/
  run-manifest.json
  inputs/
    prompt.md
    system-prompt.md
    runset-fragment.json
    initial.vforge                 # optional
    palette.json                   # optional
  transcripts/
    conversation.jsonl
    tool-calls.jsonl
    stdio.jsonl                    # only for stdio backend
    stdout.log
    stderr.log
  outputs/
    final.vforge
    model-info.json
    metrics.json
    voxel-hash.txt
    failure.json                   # only if failed
  reports/
    run-summary.md
```

Suite-level comparison writes:

```text
artifacts/benchmarks/<suite-id>/<timestamp-utc>/
  suite-manifest.json
  comparison.json
  comparison.md
```

The artifact writer must create a new timestamped run directory rather than overwriting an old run. Re-running a suite creates a new timestamp. A future `--resume` flag can skip completed run directories, but first-version behavior should be append-only.

## Run manifest

`run-manifest.json` is the durable index for a run.

```json
{
  "schema_version": 1,
  "suite_id": "primitive-builds",
  "case_id": "simple-chair",
  "variant_id": "primitive-tools",
  "trial": 1,
  "started_at_utc": "2026-04-27T00:00:00Z",
  "ended_at_utc": "2026-04-27T00:01:20Z",
  "status": "succeeded",
  "backend": "mcp-tool-loop",
  "git_commit": "30e4f81c7afefc454da4adf8e3ff38a01246e5a5",
  "working_tree_dirty": true,
  "provider": {
    "name": "configured-chat-client",
    "model": "local-model-name-or-provider-model-id",
    "temperature": 0.2,
    "seed": null
  },
  "tool_preset": "mcp-authoring-with-primitives-v1",
  "prompt_sha256": "...",
  "system_prompt_sha256": "...",
  "tool_schema_sha256": "...",
  "initial_model_sha256": null,
  "final_model_sha256": "...",
  "transcript_sha256": "...",
  "metrics_sha256": "...",
  "elapsed_ms": 80000,
  "max_rounds": 12,
  "llm_rounds": 7,
  "tool_call_count": 19,
  "error_count": 0
}
```

Secrets must never be written to artifacts. Provider configuration may record provider/model labels and non-secret generation parameters only.

## Transcript format

Use JSON Lines so large transcripts are append-friendly.

### `conversation.jsonl`

Each line is one envelope:

```json
{"index":1,"role":"system","content":"...","timestamp_utc":"..."}
{"index":2,"role":"user","content":"...","timestamp_utc":"..."}
{"index":3,"role":"assistant","content":"...","tool_call_ids":["call-1"],"timestamp_utc":"..."}
{"index":4,"role":"tool","tool_call_id":"call-1","name":"set_voxels","ok":true,"content":"Set 12 voxels.","timestamp_utc":"..."}
```

### `tool-calls.jsonl`

Each line records normalized tool use:

```json
{
  "index": 1,
  "round": 1,
  "tool_call_id": "call-1",
  "name": "apply_voxel_primitives",
  "arguments": { "primitives": [] },
  "ok": true,
  "result_summary": "Generated 64 voxels.",
  "duration_ms": 12
}
```

For MCP execution, capture the MCP method/tool name and arguments. For in-process `ToolLoop`, capture the `ToolCall` and `ToolResultContent` values. For stdio execution, capture the command and args in `stdio.jsonl`.

## Metrics

`metrics.json` should be computed from the final model through Core/App read-only services or MCP read-only tools. The first implementation should include:

- `voxel_count`
- `bounds_min` and `bounds_max`
- `grid_hint`
- `palette_usage`: count per palette index/material name
- `region_count`
- `labeled_voxel_count`
- `animation_clip_count`
- `connected_component_count_6` for occupied voxels when cheap enough for small benchmark cases
- `tool_call_count`
- `failed_tool_call_count`
- `undoable_mutation_count` when available from the transcript
- `final_model_sha256`
- `normalized_voxel_hash`

The normalized voxel hash should be independent of serializer formatting:

```text
sha256(join('\n', sorted("x,y,z,paletteIndex" for each occupied voxel)))
```

This lets comparison reports detect identical geometry even if `.vforge` formatting changes.

## Comparison workflow

After all runs finish, the suite comparison reads run manifests and metrics and writes `comparison.json` plus `comparison.md`.

First-version comparison should include:

- Success/failure per case and variant.
- Final voxel count table.
- Bounds table.
- Palette usage summary.
- Region/labeled voxel summary.
- Tool-call count and failed-tool count table.
- Hash equality/difference across variants.
- Links to `final.vforge`, transcripts, metrics, and run summaries.

The comparison report does not claim subjective quality. It highlights differences for human inspection and regression tracking.

Example `comparison.md` structure:

```markdown
# Benchmark comparison: primitive-builds

## Summary

| Case | Variant | Status | Voxels | Bounds | Tools | Failed tools | Hash |
|---|---|---:|---:|---|---:|---:|---|
| simple-chair | baseline-tools | succeeded | 42 | (0,0,0)..(5,6,5) | 18 | 0 | abc123 |
| simple-chair | primitive-tools | succeeded | 45 | (0,0,0)..(5,6,5) | 5 | 0 | def456 |

## Notes for human review

- Open each `final.vforge` in VoxelForge for visual inspection.
- Compare transcript tool counts to understand token/tool efficiency.
```

## Failure handling

A run can fail without failing the entire suite. The runner should continue to the next run unless `--fail-fast` is passed.

Failure artifacts:

- `run-manifest.json` with `status: "failed"`.
- `outputs/failure.json` containing exception type, message, phase, and safe stack trace when available.
- Partial transcripts/logs up to the failure point.

The comparison report should include failed runs with failure summaries.

## CLI shape

Initial CLI commands:

```bash
# Run a full suite and write artifacts under the runset artifact_root.
dotnet run --project src/VoxelForge.Evaluation -- run benchmarks/runsets/primitive-builds.json

# Compare existing run artifacts without executing models.
dotnet run --project src/VoxelForge.Evaluation -- compare artifacts/benchmarks/primitive-builds/20260427T000000Z

# Print the resolved execution plan without calling models.
dotnet run --project src/VoxelForge.Evaluation -- plan benchmarks/runsets/primitive-builds.json
```

Useful first-version flags:

- `--artifact-root <dir>` overrides the runset artifact root.
- `--case <case-id>` filters cases.
- `--variant <variant-id>` filters variants.
- `--trials <n>` overrides trial count.
- `--backend <mcp-tool-loop|stdio>` chooses execution backend.
- `--fail-fast` stops on the first failed run.
- `--dry-run` validates inputs and prints the plan without executing.

## Provider configuration

Provider credentials and endpoints should come from environment variables or local ignored config, not from runsets.

The runset records logical provider/model labels. The provider binding layer resolves those labels locally.

Example local ignored file (not committed):

```json
{
  "providers": {
    "configured-chat-client": {
      "kind": "openai-compatible",
      "endpoint_env": "VOXELFORGE_EVAL_ENDPOINT",
      "api_key_env": "VOXELFORGE_EVAL_API_KEY"
    }
  }
}
```

The committed design does not require choosing a specific provider. Follow-up implementation should reuse `ICompletionService` and `ChatClientCompletionService` where possible, keeping SDK-specific types in `VoxelForge.LLM`.

## Tool presets

Tool presets define which tools are available to a run. They are important for comparing surface changes such as precise voxel edits versus high-level primitives.

Example presets:

- `mcp-core-v1`: lifecycle, palette, precise voxel set/remove/query, region, spatial query.
- `mcp-authoring-v1`: `mcp-core-v1` plus planned high-level authoring tools.
- `mcp-authoring-with-primitives-v1`: includes `apply_voxel_primitives` once task 752 follow-ups implement it.
- `stdio-console-v1`: console command surface only.

A run manifest records the preset id and a hash of the concrete tool schemas exposed during the run. That hash makes changes to tool shape visible in benchmark results.

## Relationship to headless and stdio architecture

The harness reuses existing headless surfaces rather than adding editor-specific benchmark shortcuts:

- `VoxelForge.Mcp` remains the preferred headless tool server for MCP-compatible agents.
- `StdioHost` remains the JSON-line command adapter for command-oriented agents.
- `ToolLoop` remains the reusable call-LLM, dispatch tools, repeat engine for in-process evaluations.
- `LlmToolApplicationService` remains the path for applying LLM mutation intents through App services and undoable commands.

The harness itself is an adapter/orchestrator. It owns run scheduling and artifact writing, not voxel editing behavior.

## Test strategy for implementation

Follow-up implementation should add tests before relying on live models:

- Runset parsing validates required fields and rejects unsafe paths.
- Artifact writer creates the documented layout without overwriting prior runs.
- Metrics service computes stable normalized voxel hashes for equivalent models.
- Comparison service produces deterministic JSON/Markdown summaries from fixture manifests.
- Stdio backend can run against a fake process adapter or captured transcript fixture.
- In-process backend can use `FakeCompletionService` and fake tool calls.
- Architecture tests ensure `VoxelForge.Evaluation` does not reference `VoxelForge.Engine.MonoGame`, FNA, or Myra.

Live provider tests should remain manual or explicitly opt-in because they require credentials, network/local model availability, and can be nondeterministic.

## Follow-up implementation split

1. **Evaluation project and runset parser**
   - Add `src/VoxelForge.Evaluation` console project.
   - Define runset DTOs and validation.
   - Implement `plan` and `--dry-run`.
   - Add tests for parsing, filtering, and path safety.

2. **Artifact writer and metrics service**
   - Implement documented artifact layout.
   - Save run manifests, copied inputs, logs, transcripts, and metrics.
   - Compute normalized voxel hash and basic model metrics from Core/App state or MCP read-only tool results.
   - Add fixture-based tests.

3. **Comparison reporter**
   - Read run manifests/metrics and produce `comparison.json` plus `comparison.md`.
   - Include success/failure, voxel counts, bounds, palette usage, tool counts, and hash differences.
   - Add deterministic snapshot-style tests using committed fixtures.

4. **MCP tool-loop execution backend**
   - Run isolated headless MCP sessions per run.
   - Bind configured model/provider to exposed MCP tools.
   - Capture conversation/tool transcripts.
   - Save final `.vforge` through lifecycle tools.
   - Add fake completion/MCP fixture tests; keep live model tests opt-in.

5. **Stdio command backend**
   - Launch headless stdio process with redirected stdin/stdout.
   - Record JSON-line command request/response transcripts.
   - Save final `.vforge` through existing console save command.
   - Add process-adapter tests without requiring Engine renderer.

6. **Optional screenshot post-process**
   - Add a separate opt-in command that opens `.vforge` outputs in the Engine project and captures screenshots.
   - Keep it out of required benchmark runs and out of `VoxelForge.Evaluation` core comparisons.

## Acceptance checklist

A follow-up implementation of this design is complete when:

- A runset can execute a case/variant matrix locally.
- Every run writes `run-manifest.json`, inputs, transcript, final `.vforge`, metrics, and summary report.
- Suite comparison writes `comparison.json` and `comparison.md`.
- The first-version comparison uses final `.vforge` outputs, metrics, and transcripts, not screenshots.
- Live model execution is optional and provider credentials are never committed or written into artifacts.
- No benchmark code references Engine/FNA/Myra unless it is the separate optional screenshot post-process.
