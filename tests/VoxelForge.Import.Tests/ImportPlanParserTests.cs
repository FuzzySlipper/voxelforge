using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;
using VoxelForge.Evaluation;

namespace VoxelForge.Import.Tests;

public sealed class ImportPlanParserTests
{
    [Fact]
    public void Normalize_AcceptsCanonicalToolEnvelope()
    {
        using var fixture = TempFile.Json("""
        {
          "name": "set_voxels",
          "arguments": {
            "voxels": [ { "x": 0, "y": 0, "z": 0, "i": 1 } ]
          }
        }
        """);

        ImportNormalizeResult result = Normalize(fixture.Path);

        AssertSuccess(result, "tool-envelope", 1);
        ImportPlanOperation operation = result.Plan!.Operations[0];
        Assert.Equal("op-000001", operation.OperationId);
        Assert.Equal("tool_call", operation.Kind);
        Assert.Equal("set_voxels", operation.Name);
        Assert.Equal("mutation", operation.Effect);
        Assert.Equal(1, operation.Arguments.GetProperty("voxels").GetArrayLength());
    }

    [Fact]
    public void Normalize_AcceptsProviderEnvelopeAliases()
    {
        using var toolNameFixture = TempFile.Json("""
        {
          "tool_name": "set_grid_hint",
          "arguments": { "size": 24 }
        }
        """);
        using var functionFixture = TempFile.Json("""
        {
          "function": {
            "name": "remove_voxels",
            "arguments": "{\"positions\":[{\"x\":2,\"y\":0,\"z\":0}]}"
          }
        }
        """);
        using var toolUseFixture = TempFile.Json("""
        {
          "type": "tool_use",
          "name": "set_palette_entry",
          "input": { "index": 1, "name": "stone", "r": 64, "g": 64, "b": 64 }
        }
        """);
        using var mcpFixture = TempFile.Json("""
        {
          "method": "tools/call",
          "params": {
            "name": "apply_voxel_primitives",
            "arguments": { "primitives": [] }
          }
        }
        """);

        ImportNormalizeResult toolNameResult = Normalize(toolNameFixture.Path);
        ImportNormalizeResult functionResult = Normalize(functionFixture.Path);
        ImportNormalizeResult toolUseResult = Normalize(toolUseFixture.Path);
        ImportNormalizeResult mcpResult = Normalize(mcpFixture.Path);

        AssertSuccess(toolNameResult, "tool-envelope", 1);
        AssertSuccess(functionResult, "tool-envelope", 1);
        AssertSuccess(toolUseResult, "tool-envelope", 1);
        AssertSuccess(mcpResult, "tool-envelope", 1);
        Assert.Equal("set_grid_hint", toolNameResult.Plan!.Operations[0].Name);
        Assert.Equal("remove_voxels", functionResult.Plan!.Operations[0].Name);
        Assert.Equal(1, functionResult.Plan.Operations[0].Arguments.GetProperty("positions").GetArrayLength());
        Assert.Equal("set_palette_entry", toolUseResult.Plan!.Operations[0].Name);
        Assert.Equal("apply_voxel_primitives", mcpResult.Plan!.Operations[0].Name);
    }

    [Fact]
    public void Normalize_AcceptsRawArgumentsWhenToolIsExplicit()
    {
        using var fixture = TempFile.Json("""
        {
          "voxels": [ { "x": 1, "y": 2, "z": 3, "i": 4 } ]
        }
        """);

        ImportNormalizeResult result = Normalize(fixture.Path, new ImportNormalizeOptions
        {
            Format = ImportInputFormat.RawArguments,
            ToolName = "set_voxels",
            CapturedAtUtc = FixedTime,
        });

        AssertSuccess(result, "raw-arguments", 1);
        Assert.Equal("set_voxels", result.Plan!.Operations[0].Name);
        Assert.Equal(1, result.Plan.Operations[0].Arguments.GetProperty("voxels")[0].GetProperty("x").GetInt32());
    }

    [Fact]
    public void Normalize_AcceptsToolCallArrayAndBatchObject()
    {
        using var arrayFixture = TempFile.Json("""
        [
          { "name": "set_voxels", "arguments": { "voxels": [] } },
          { "name": "describe_model", "arguments": {} }
        ]
        """);
        using var batchFixture = TempFile.Json("""
        {
          "tool_calls": [
            { "id": "call-1", "tool_name": "set_grid_hint", "arguments": { "size": 32 } }
          ]
        }
        """);

        ImportNormalizeResult arrayResult = Normalize(arrayFixture.Path);
        ImportNormalizeResult batchResult = Normalize(batchFixture.Path);

        AssertSuccess(arrayResult, "tool-call-array", 2, expectedWarnings: 1);
        Assert.Equal("read_only", arrayResult.Plan!.Operations[1].Effect);
        Assert.Contains(arrayResult.Diagnostics, DiagnosticCode("IMPORT201"));

        AssertSuccess(batchResult, "tool-call-array", 1);
        Assert.Equal("call-1", batchResult.Plan!.Operations[0].SourceCallId);
        Assert.Equal("set_grid_hint", batchResult.Plan.Operations[0].Name);
    }

    [Fact]
    public void Normalize_AcceptsToolCallsJsonlWithSourceLocations()
    {
        using var fixture = TempFile.Text("""
        {"index":1,"round":1,"tool_call_id":"call-1","name":"set_voxels","arguments":{"voxels":[{"x":0,"y":0,"z":0,"i":1}]},"ok":true}
        {"index":2,"round":1,"tool_call_id":"call-2","name":"get_model_info","arguments":{},"ok":true}
        """, ".jsonl");

        ImportNormalizeResult result = Normalize(fixture.Path);

        AssertSuccess(result, "tool-calls-jsonl", 2, expectedWarnings: 1);
        Assert.Equal(1, result.Plan!.Operations[0].SourceLine);
        Assert.Equal(2, result.Plan.Operations[1].SourceIndex);
        Assert.Equal("call-2", result.Plan.Operations[1].SourceCallId);
        Assert.Contains(result.Diagnostics, DiagnosticCode("IMPORT201"));
    }

    [Fact]
    public void Normalize_AcceptsStdioJsonlWithCommandRequests()
    {
        using var fixture = TempFile.Text("""
        {"index":1,"request":{"command":"set","args":["0","0","0","1"]},"response":{"ok":true,"message":"Set"}}
        {"index":2,"request":{"command":"undo","args":[]},"response":{"ok":false,"message":"No undo"}}
        """, ".jsonl");

        ImportNormalizeResult result = Normalize(fixture.Path);

        AssertSuccess(result, "stdio-jsonl", 2, expectedWarnings: 2);
        Assert.Equal("console_command", result.Plan!.Operations[0].Kind);
        Assert.Equal("set", result.Plan.Operations[0].Name);
        Assert.Equal("undo", result.Plan.Operations[1].Name);
        Assert.Contains(result.Diagnostics, DiagnosticCode("IMPORT202"));
        Assert.Contains(result.Diagnostics, DiagnosticCode("IMPORT203"));
    }

    [Fact]
    public void Normalize_ReportsMalformedJsonMissingFieldsUnsupportedToolsAndCaps()
    {
        using var malformed = TempFile.Text("{ nope", ".json");
        using var missingTool = TempFile.Json("{ \"arguments\": {} }");
        using var unsupported = TempFile.Json("{ \"name\": \"save_model\", \"arguments\": {} }");
        using var oversized = TempFile.Json("{ \"name\": \"set_voxels\", \"arguments\": { \"voxels\": [ {}, {} ] } }");

        ImportNormalizeResult malformedResult = Normalize(malformed.Path);
        ImportNormalizeResult missingResult = Normalize(missingTool.Path, new ImportNormalizeOptions { Format = ImportInputFormat.ToolEnvelope, CapturedAtUtc = FixedTime });
        ImportNormalizeResult unsupportedResult = Normalize(unsupported.Path);
        ImportNormalizeResult oversizedResult = Normalize(oversized.Path, new ImportNormalizeOptions { MaxGeneratedVoxels = 1, CapturedAtUtc = FixedTime });

        AssertFailure(malformedResult, "IMPORT001");
        AssertFailure(missingResult, "IMPORT011");
        AssertFailure(unsupportedResult, "IMPORT101");
        AssertFailure(oversizedResult, "IMPORT103");

        ImportDiagnostic missingDiagnostic = Assert.Single(missingResult.Diagnostics);
        Assert.Equal(missingTool.Path, missingDiagnostic.SourcePath);
        Assert.Equal(1, missingDiagnostic.OperationIndex);
        Assert.Equal("/name", missingDiagnostic.JsonPointer);
    }

    [Fact]
    public void Normalize_ReportsOperationValidationFailures()
    {
        using var missingCoordinate = TempFile.Json("{ \"name\": \"set_voxels\", \"arguments\": { \"voxels\": [ { \"x\": 0, \"z\": 0, \"i\": 1 } ] } }");
        using var invalidPalette = TempFile.Json("{ \"name\": \"set_palette_entry\", \"arguments\": { \"index\": 0, \"name\": \"air\" } }");
        using var missingPrimitives = TempFile.Json("{ \"name\": \"apply_voxel_primitives\", \"arguments\": { } }");

        ImportNormalizeResult coordinateResult = Normalize(missingCoordinate.Path);
        ImportNormalizeResult paletteResult = Normalize(invalidPalette.Path);
        ImportNormalizeResult primitivesResult = Normalize(missingPrimitives.Path);

        AssertFailure(coordinateResult, "IMPORT111");
        AssertFailure(paletteResult, "IMPORT112");
        AssertFailure(primitivesResult, "IMPORT111");
        Assert.Contains(coordinateResult.Diagnostics, diagnostic => diagnostic.JsonPointer == "/arguments/voxels/0/y");
        Assert.Contains(paletteResult.Diagnostics, diagnostic => diagnostic.JsonPointer == "/arguments/index");
        Assert.Contains(primitivesResult.Diagnostics, diagnostic => diagnostic.JsonPointer == "/arguments/primitives");
    }

    [Fact]
    public void Normalize_ReportsJsonlLineAndFilesystemCommandDiagnostics()
    {
        using var fixture = TempFile.Text("""
        {"index":1,"request":{"command":"set","args":["0","0","0","1"]},"response":{"ok":true}}
        { bad json
        {"index":3,"request":{"command":"save","args":["external.vforge"]},"response":{"ok":true}}
        """, ".jsonl");

        ImportNormalizeResult result = Normalize(fixture.Path, new ImportNormalizeOptions { Format = ImportInputFormat.StdioJsonl, CapturedAtUtc = FixedTime });

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, DiagnosticCode("IMPORT002"));
        Assert.Contains(result.Diagnostics, DiagnosticCode("IMPORT102"));
        ImportDiagnostic malformedLine = Assert.Single(result.Diagnostics, DiagnosticCode("IMPORT002"));
        Assert.Equal(2, malformedLine.Line);
        ImportDiagnostic saveCommand = Assert.Single(result.Diagnostics, DiagnosticCode("IMPORT102"));
        Assert.Equal(3, saveCommand.Line);
        Assert.Equal(3, saveCommand.OperationIndex);
        Assert.Equal("save", saveCommand.ToolName);
        Assert.Equal("/request/command", saveCommand.JsonPointer);
    }

    [Fact]
    public void Normalize_WritesProviderNeutralPlanJsonWithLowercaseDiagnostics()
    {
        using var fixture = TempFile.Json("{ \"name\": \"describe_model\", \"arguments\": {} }");
        ImportNormalizeResult result = Normalize(fixture.Path);

        string planJson = JsonSerializer.Serialize(result.Plan, ImportJson.SerializerOptions);
        string reportJson = JsonSerializer.Serialize(result.Report, ImportJson.SerializerOptions);

        Assert.Contains("\"schema_version\": 1", planJson, StringComparison.Ordinal);
        Assert.Contains("\"operations\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"effect\": \"read_only\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"severity\": \"warning\"", reportJson, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenAI", planJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_key", planJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_NormalizeWritesPlanAndValidateWritesReport()
    {
        using var input = TempFile.Json("{ \"name\": \"set_palette_entry\", \"arguments\": { \"index\": 1, \"name\": \"stone\", \"r\": 64, \"g\": 64, \"b\": 64 } }");
        using var planOut = TempFile.Empty(".plan.json");
        using var normalizeReportOut = TempFile.Empty(".normalize-report.json");
        using var validateReportOut = TempFile.Empty(".validate-report.json");
        var normalizeOutput = new StringWriter();
        var normalizeError = new StringWriter();
        var validateOutput = new StringWriter();
        var validateError = new StringWriter();

        int normalizeExit = new ImportCli().Execute([
            "normalize",
            input.Path,
            "--format",
            "auto",
            "--out",
            planOut.Path,
            "--report-out",
            normalizeReportOut.Path,
        ], normalizeOutput, normalizeError);
        int validateExit = new ImportCli().Execute([
            "validate",
            input.Path,
            "--format",
            "auto",
            "--report-out",
            validateReportOut.Path,
        ], validateOutput, validateError);

        Assert.Equal(0, normalizeExit);
        Assert.Equal(string.Empty, normalizeError.ToString());
        Assert.Contains("Wrote import plan", normalizeOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("set_palette_entry", File.ReadAllText(planOut.Path), StringComparison.Ordinal);
        Assert.Contains("\"status\": \"succeeded\"", File.ReadAllText(normalizeReportOut.Path), StringComparison.Ordinal);

        Assert.Equal(0, validateExit);
        Assert.Equal(string.Empty, validateError.ToString());
        Assert.Contains("Status: succeeded", validateOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Operations: 1", validateOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"status\": \"succeeded\"", File.ReadAllText(validateReportOut.Path), StringComparison.Ordinal);
        Assert.DoesNotContain("Wrote import plan", validateOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cli_ReplayMaterializesPlanThroughToolAndConsoleRoutes()
    {
        using var plan = TempFile.Json("""
        {
          "schema_version": 1,
          "source": { "format": "tool-call-array", "path": "fixture.json", "sha256": "fixture", "captured_at_utc": "2026-04-27T00:00:00+00:00" },
          "options": { "strict": true, "max_operations": 100, "max_generated_voxels": 1000 },
          "operations": [
            { "operation_id": "op-000001", "source_index": 1, "kind": "tool_call", "name": "describe_model", "arguments": {}, "effect": "read_only" },
            { "operation_id": "op-000002", "source_index": 2, "kind": "tool_call", "name": "new_model", "arguments": { "name": "imported", "grid_hint": 16 }, "effect": "lifecycle" },
            { "operation_id": "op-000003", "source_index": 3, "kind": "tool_call", "name": "set_palette_entry", "arguments": { "index": 1, "name": "stone", "r": 64, "g": 64, "b": 64 }, "effect": "mutation" },
            { "operation_id": "op-000004", "source_index": 4, "kind": "tool_call", "name": "fill_box", "arguments": { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0, "z2": 0, "palette_index": 1 }, "effect": "mutation" },
            { "operation_id": "op-000005", "source_index": 5, "kind": "tool_call", "name": "set_voxels", "arguments": { "voxels": [ { "x": 2, "y": 0, "z": 0, "i": 1 } ] }, "effect": "mutation" },
            { "operation_id": "op-000006", "source_index": 6, "kind": "tool_call", "name": "remove_voxels", "arguments": { "positions": [ { "x": 1, "y": 0, "z": 0 } ] }, "effect": "mutation" },
            { "operation_id": "op-000007", "source_index": 7, "kind": "tool_call", "name": "create_region", "arguments": { "name": "body" }, "effect": "mutation" },
            { "operation_id": "op-000008", "source_index": 8, "kind": "tool_call", "name": "assign_voxels_to_region", "arguments": { "region_id": "body", "positions": [ { "x": 0, "y": 0, "z": 0 }, { "x": 2, "y": 0, "z": 0 } ] }, "effect": "mutation" },
            { "operation_id": "op-000009", "source_index": 9, "kind": "console_command", "name": "palette", "arguments": { "command": "palette", "args": ["add", "2", "glass", "10", "20", "30"] }, "effect": "mutation" },
            { "operation_id": "op-000010", "source_index": 10, "kind": "console_command", "name": "set", "arguments": { "command": "set", "args": ["3", "0", "0", "2"] }, "effect": "mutation" },
            { "operation_id": "op-000011", "source_index": 11, "kind": "console_command", "name": "label", "arguments": { "command": "label", "args": ["wing", "3", "0", "0"] }, "effect": "mutation" }
          ]
        }
        """);
        string outputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.Path)!, "materialized.vforge");
        string reportPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.Path)!, "report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute(["replay", plan.Path, "--out", outputPath, "--report-out", reportPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists(outputPath));
        Assert.Contains("Wrote materialized model", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("IMPORT201", File.ReadAllText(reportPath), StringComparison.Ordinal);

        var (model, labels, _, meta) = ReadProject(outputPath);
        Assert.Equal("materialized", meta.Name);
        Assert.Equal(16, model.GridHint);
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Null(model.GetVoxel(new Point3(1, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(2, 0, 0)));
        Assert.Equal((byte)2, model.GetVoxel(new Point3(3, 0, 0)));
        Assert.Equal("stone", model.Palette.Get(1)?.Name);
        Assert.Equal("glass", model.Palette.Get(2)?.Name);
        Assert.True(labels.Regions.TryGetValue(new RegionId("body"), out RegionDef? body));
        Assert.Equal(2, body.Voxels.Count);
        Assert.True(labels.Regions.TryGetValue(new RegionId("wing"), out RegionDef? wing));
        Assert.Single(wing.Voxels);
    }

    [Fact]
    public void Cli_ImportOneShotNormalizesAndMaterializesToolCalls()
    {
        using var input = TempFile.Json("""
        [
          { "name": "set_palette_entry", "arguments": { "index": 1, "name": "brick", "r": 120, "g": 40, "b": 30 } },
          { "name": "apply_voxel_primitives", "arguments": { "primitives": [ { "kind": "block", "palette_index": 1, "at": { "x": 4, "y": 0, "z": 0 } } ] } }
        ]
        """);
        string outputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(input.Path)!, "imported.vforge");
        string planOutPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(input.Path)!, "imported.plan.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute(["import", input.Path, "--format", "auto", "--out", outputPath, "--plan-out", planOutPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists(outputPath));
        Assert.True(File.Exists(planOutPath));
        Assert.Contains("apply_voxel_primitives", File.ReadAllText(planOutPath), StringComparison.Ordinal);
        var (model, _, _, _) = ReadProject(outputPath);
        Assert.Equal((byte)1, model.GetVoxel(new Point3(4, 0, 0)));
    }

    [Fact]
    public void Cli_ImportBenchmarkToolCallsFixtureMaterializesPrimitiveModelAndPreservesReportProvenance()
    {
        string inputPath = FixturePath("benchmark-tool-calls.jsonl");
        using var outputFile = TempFile.Empty(".vforge");
        using var planFile = TempFile.Empty(".plan.json");
        using var reportFile = TempFile.Empty(".report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute([
            "import",
            inputPath,
            "--format",
            "tool-calls-jsonl",
            "--out",
            outputFile.Path,
            "--plan-out",
            planFile.Path,
            "--report-out",
            reportFile.Path,
        ], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        VoxelForgeImportPlan plan = ReadJson<VoxelForgeImportPlan>(planFile.Path);
        ImportReport report = ReadJson<ImportReport>(reportFile.Path);
        ImportPlanOperation primitiveOperation = Assert.Single(plan.Operations, operation => operation.Name == "apply_voxel_primitives");
        Assert.Equal(12, primitiveOperation.SourceIndex);
        Assert.Equal(3, primitiveOperation.SourceLine);
        Assert.Equal("call-primitive", primitiveOperation.SourceCallId);
        Assert.Contains(report.Operations, operation => operation.SourceCallId == "call-primitive" && operation.SourceIndex == 12);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT201" && diagnostic.SourceCallId == "call-info" && diagnostic.OperationIndex == 13);

        var (model, labels, clips, _) = ReadProject(outputFile.Path);
        Assert.Equal(4, model.GetVoxelCount());
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(1, 1, 0)));
        Assert.Equal("brick", model.Palette.Get(1)?.Name);

        var metrics = new BenchmarkMetricsService().Compute(model, labels, clips, new BenchmarkMetricsOptions
        {
            ToolCallCount = plan.Operations.Count,
            FailedToolCallCount = 0,
            UndoableMutationCount = 0,
        });
        Assert.Equal(4, metrics.VoxelCount);
        BenchmarkPaletteUsageMetric paletteUsage = Assert.Single(metrics.PaletteUsage);
        Assert.Equal(4, paletteUsage.VoxelCount);
        Assert.False(string.IsNullOrWhiteSpace(metrics.NormalizedVoxelHash));
    }

    [Fact]
    public void Cli_ImportBenchmarkStdioFixtureMaterializesExpectedModelAndPreservesSourceIndices()
    {
        string inputPath = FixturePath("benchmark-stdio.jsonl");
        using var outputFile = TempFile.Empty(".vforge");
        using var planFile = TempFile.Empty(".plan.json");
        using var reportFile = TempFile.Empty(".report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute([
            "import",
            inputPath,
            "--format",
            "stdio-jsonl",
            "--out",
            outputFile.Path,
            "--plan-out",
            planFile.Path,
            "--report-out",
            reportFile.Path,
        ], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        VoxelForgeImportPlan plan = ReadJson<VoxelForgeImportPlan>(planFile.Path);
        ImportReport report = ReadJson<ImportReport>(reportFile.Path);
        Assert.Equal("stdio-jsonl", plan.Source.Format);
        Assert.Equal([21, 22, 23], plan.Operations.Select(operation => operation.SourceIndex).ToArray());
        Assert.Equal([21, 22, 23], report.Operations.Select(operation => operation.SourceIndex).ToArray());

        var (model, _, _, _) = ReadProject(outputFile.Path);
        Assert.Equal(3, model.GetVoxelCount());
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(1, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(2, 0, 0)));
        Assert.Equal("wood", model.Palette.Get(1)?.Name);
    }

    [Fact]
    public void Cli_ImportExpandedBenchmarkStdioFixtureCoversSupportedCommandReplay()
    {
        string inputPath = FixturePath("benchmark-stdio-expanded.jsonl");
        using var outputFile = TempFile.Empty(".vforge");
        using var planFile = TempFile.Empty(".plan.json");
        using var reportFile = TempFile.Empty(".report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute([
            "import",
            inputPath,
            "--format",
            "stdio-jsonl",
            "--out",
            outputFile.Path,
            "--plan-out",
            planFile.Path,
            "--report-out",
            reportFile.Path,
        ], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        VoxelForgeImportPlan plan = ReadJson<VoxelForgeImportPlan>(planFile.Path);
        ImportReport report = ReadJson<ImportReport>(reportFile.Path);
        Assert.Equal(12, plan.Operations.Count);
        Assert.Equal([41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52], report.Operations.Select(operation => operation.SourceIndex).ToArray());
        Assert.Contains(report.Operations, operation => operation.Name == "remove");
        Assert.Contains(report.Operations, operation => operation.Name == "clear");
        Assert.Contains(report.Operations, operation => operation.Name == "grid");
        Assert.Contains(report.Operations, operation => operation.Name == "undo");
        Assert.Contains(report.Operations, operation => operation.Name == "redo");
        Assert.Contains(report.Operations, operation => operation.Name == "label");
        Assert.Contains(report.Operations, operation => operation.Name == "regions");
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT202" && diagnostic.OperationIndex == 46);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT202" && diagnostic.OperationIndex == 47);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT202" && diagnostic.OperationIndex == 51);

        var (model, labels, _, _) = ReadProject(outputFile.Path);
        Assert.Equal(32, model.GridHint);
        Assert.Equal(3, model.GetVoxelCount());
        Assert.Equal((byte)1, model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Null(model.GetVoxel(new Point3(1, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(2, 0, 0)));
        Assert.Equal((byte)1, model.GetVoxel(new Point3(3, 0, 0)));
        Assert.True(labels.Regions.TryGetValue(new RegionId("body"), out RegionDef? body));
        Assert.Single(body.Voxels);
        Assert.Contains(new Point3(0, 0, 0), body.Voxels);
    }

    [Fact]
    public void Cli_ValidateBenchmarkToolCallFailureReportPreservesCallIdAndWarning()
    {
        string inputPath = FixturePath("benchmark-tool-calls-failed.jsonl");
        using var reportFile = TempFile.Empty(".report.json");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute([
            "validate",
            inputPath,
            "--format",
            "tool-calls-jsonl",
            "--report-out",
            reportFile.Path,
        ], output, error);

        Assert.Equal(1, exitCode);
        ImportReport report = ReadJson<ImportReport>(reportFile.Path);
        Assert.Equal("failed", report.Status);
        Assert.Contains(report.Operations, operation => operation.SourceCallId == "call-bad-primitive" && operation.SourceIndex == 31);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT111" && diagnostic.SourceCallId == "call-bad-primitive" && diagnostic.OperationIndex == 31);
        Assert.Contains(report.Diagnostics, diagnostic => diagnostic.Code == "IMPORT203" && diagnostic.SourceCallId == "call-bad-primitive" && diagnostic.OperationIndex == 31);
    }

    [Fact]
    public void Cli_ReplayRejectsMalformedPlanWithoutWritingOutput()
    {
        using var plan = TempFile.Json("""
        {
          "schema_version": 1,
          "source": { "format": "tool-call-array", "path": "fixture.json", "sha256": "fixture", "captured_at_utc": "2026-04-27T00:00:00+00:00" },
          "options": { "strict": true, "max_operations": 100, "max_generated_voxels": 1000 },
          "operations": [
            { "operation_id": "op-000001", "source_index": 1, "kind": "tool_call", "name": "new_model", "arguments": { "grid_hint": 12 }, "effect": "lifecycle" }
          ]
        }
        """);
        string outputPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(plan.Path)!, "should-not-exist.vforge");
        var output = new StringWriter();
        var error = new StringWriter();

        int exitCode = new ImportCli().Execute(["replay", plan.Path, "--out", outputPath], output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.False(File.Exists(outputPath));
        Assert.Contains("IMPORT111", error.ToString(), StringComparison.Ordinal);
    }

    private static ImportNormalizeResult Normalize(string path)
    {
        return Normalize(path, new ImportNormalizeOptions { CapturedAtUtc = FixedTime });
    }

    private static ImportNormalizeResult Normalize(string path, ImportNormalizeOptions options)
    {
        var parser = new ImportPlanParser(new ImportPlanValidator());
        return parser.NormalizeFile(path, options);
    }

    private static (VoxelModel Model, LabelIndex Labels, List<AnimationClip> Clips, ProjectMetadata Meta) ReadProject(string path)
    {
        var serializer = new ProjectSerializer(NullLoggerFactory.Instance);
        return serializer.Deserialize(File.ReadAllText(path));
    }

    private static T ReadJson<T>(string path)
    {
        T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), ImportJson.SerializerOptions);
        Assert.NotNull(value);
        return value;
    }

    private static string FixturePath(string fileName)
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    }

    private static void AssertSuccess(ImportNormalizeResult result, string sourceFormat, int operationCount, int expectedWarnings = 0)
    {
        Assert.True(result.Success, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Plan);
        Assert.Equal(sourceFormat, result.Plan.Source.Format);
        Assert.Equal(operationCount, result.Plan.Operations.Count);
        Assert.Equal(0, result.Report.ErrorCount);
        Assert.Equal(expectedWarnings, result.Report.WarningCount);
        Assert.Equal(FixedTime.ToString("O"), result.Plan.Source.CapturedAtUtc);
    }

    private static void AssertFailure(ImportNormalizeResult result, string expectedCode)
    {
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, DiagnosticCode(expectedCode));
    }

    private static Predicate<ImportDiagnostic> DiagnosticCode(string code)
    {
        return diagnostic => diagnostic.Code == code;
    }

    private static string FormatDiagnostics(IReadOnlyList<ImportDiagnostic> diagnostics)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message));
    }

    private static readonly DateTimeOffset FixedTime = new(2026, 4, 27, 0, 0, 0, TimeSpan.Zero);

    private sealed class TempFile : IDisposable
    {
        private readonly string _directory;

        private TempFile(string suffix)
        {
            _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "voxelforge-import-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "input" + suffix);
        }

        public string Path { get; }

        public static TempFile Json(string text)
        {
            return Text(text, ".json");
        }

        public static TempFile Text(string text, string suffix)
        {
            var file = new TempFile(suffix);
            File.WriteAllText(file.Path, NormalizeNewlines(text));
            return file;
        }

        public static TempFile Empty(string suffix)
        {
            var file = new TempFile(suffix);
            File.WriteAllText(file.Path, string.Empty);
            return file;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal);
        }
    }
}
