using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using VoxelForge.App;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Reference;
using VoxelForge.Core.Services;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Services;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class McpToolTests
{
    [Fact]
    public void ToolRegistry_RegistersMcpToolsExplicitly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-registry");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new EditorConfigState());
        services.AddSingleton(new VoxelForgeMcpOptions());
        services.AddSingleton<VoxelForgeMcpSession>();
        services.AddSingleton<IViewerCaptureService>(new FakeViewerCaptureService(tempDir));
        services.AddVoxelForgeMcpTools();

        using var provider = services.BuildServiceProvider();

        var toolNames = provider.GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "apply_voxel_primitives",
                "assign_voxels_to_region",
                "capture_reference_views",
                "check_collision",
                "clear_model",
                "clear_reference_models",
                "console_count",
                "count_voxels",
                "create_region",
                "delete_region",
                "describe_model",
                "export_reference_state_preset",
                "fill_box",
                "fit_reference_model",
                "get_cross_section",
                "get_interface_voxels",
                "get_model_info",
                "get_reference_model_diagnostics",
                "get_region_bounds",
                "get_region_neighbors",
                "get_region_tree",
                "get_region_voxels",
                "get_voxel",
                "get_voxels_in_area",
                "import_reference_state_preset",
                "inspect_reference_materials",
                "list_console_commands",
                "list_models",
                "list_palette",
                "list_reference_models",
                "list_regions",
                "load_model",
                "load_reference_model",
                "measure_distance",
                "new_model",
                "publish_preview",
                "raycast_reference_model",
                "redo",
                "reference_model_axis_histogram",
                "remove_reference_model",
                "remove_voxels",
                "run_console_command",
                "sample_reference_model_views",
                "save_model",
                "set_grid_hint",
                "set_palette_entry",
                "set_reference_model_texture",
                "set_reference_texture_sampling",
                "set_voxels",
                "set_voxels_runs",
                "suggest_reference_transform",
                "transform_reference_model",
                "undo",
                "view_from_angle",
                "view_model",
                "voxelize_reference_model",
            ],
            toolNames);

        // Clean up
        try { Directory.Delete(tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void DescribeModelMcpTool_DelegatesToLlmHandlerWithSessionState()
    {
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(1, 2, 3), 4);
        session.Document.Model.Palette.Set(4, new MaterialDef
        {
            Name = "test",
            Color = new RgbaColor(10, 20, 30),
        });
        var tool = new DescribeModelMcpTool(
            new DescribeModelHandler(new VoxelQueryService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("Voxel model with 1 voxels", result.Message, StringComparison.Ordinal);
        Assert.Contains("test", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetModelInfoMcpTool_ReturnsJsonStats()
    {
        var session = CreateSession();
        session.Document.Model.GridHint = 16;
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 2);
        session.Document.Model.Palette.Set(2, new MaterialDef
        {
            Name = "stone",
            Color = new RgbaColor(1, 2, 3),
        });
        var tool = new GetModelInfoMcpTool(
            new GetModelInfoHandler(new VoxelQueryService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(result.Message);
        Assert.Equal(1, document.RootElement.GetProperty("voxelCount").GetInt32());
        Assert.Equal(16, document.RootElement.GetProperty("gridHint").GetInt32());
        Assert.Equal("stone", document.RootElement.GetProperty("paletteEntries")[0].GetProperty("name").GetString());
        Assert.True(tool.IsReadOnly);
    }

    [Fact]
    public void LlmMutationMcpTools_ApplyThroughUndoableSessionServices()
    {
        var session = CreateSession();
        var applicationService = new LlmToolApplicationService(new VoxelEditingService());
        var setTool = new SetVoxelsMcpTool(
            new SetVoxelsHandler(new VoxelMutationIntentService()),
            session,
            applicationService);
        var removeTool = new RemoveVoxelsMcpTool(
            new RemoveVoxelsHandler(new VoxelMutationIntentService()),
            session,
            applicationService);

        var setResult = setTool.Invoke(JsonArguments("""
        { "voxels": [ { "x": 1, "y": 2, "z": 3, "i": 7 } ] }
        """), CancellationToken.None);

        Assert.True(setResult.Success);
        Assert.Equal((byte)7, session.Document.Model.GetVoxel(new Point3(1, 2, 3)));
        Assert.True(session.UndoStack.CanUndo);
        Assert.False(setTool.IsReadOnly);

        session.UndoStack.Undo();
        Assert.Null(session.Document.Model.GetVoxel(new Point3(1, 2, 3)));
        session.UndoStack.Redo();
        Assert.Equal((byte)7, session.Document.Model.GetVoxel(new Point3(1, 2, 3)));

        var removeResult = removeTool.Invoke(JsonArguments("""
        { "positions": [ { "x": 1, "y": 2, "z": 3 } ] }
        """), CancellationToken.None);

        Assert.True(removeResult.Success);
        Assert.Null(session.Document.Model.GetVoxel(new Point3(1, 2, 3)));
        Assert.False(removeTool.IsReadOnly);
    }

    [Fact]
    public void ApplyVoxelPrimitivesMcpTool_AppliesPrimitiveBatchThroughUndoableServices()
    {
        var session = CreateSession();
        var tool = new ApplyVoxelPrimitivesMcpTool(
            new ApplyVoxelPrimitivesHandler(new VoxelPrimitiveGenerationService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(JsonArguments("""
        {
            "primitives": [
                {
                    "id": "body",
                    "kind": "box",
                    "from": { "x": 0, "y": 0, "z": 0 },
                    "to": { "x": 1, "y": 1, "z": 0 },
                    "palette_index": 2
                },
                {
                    "id": "marker",
                    "kind": "block",
                    "at": { "x": 0, "y": 0, "z": 0 },
                    "palette_index": 3
                },
                {
                    "id": "rail",
                    "kind": "line",
                    "from": { "x": 0, "y": 0, "z": 1 },
                    "to": { "x": 2, "y": 0, "z": 1 },
                    "palette_index": 4
                }
            ]
        }
        """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.False(tool.IsReadOnly);
        Assert.Contains("Generated 7 voxel assignment(s) from 3 primitive(s).", result.Message, StringComparison.Ordinal);
        Assert.Equal((byte)3, session.Document.Model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Equal((byte)2, session.Document.Model.GetVoxel(new Point3(1, 1, 0)));
        Assert.Equal((byte)4, session.Document.Model.GetVoxel(new Point3(2, 0, 1)));
        Assert.True(session.UndoStack.CanUndo);

        session.UndoStack.Undo();
        Assert.Equal(0, session.Document.Model.GetVoxelCount());
        session.UndoStack.Redo();
        Assert.Equal(7, session.Document.Model.GetVoxelCount());
    }

    [Fact]
    public void ApplyVoxelPrimitivesMcpTool_PreviewOnlyDoesNotMutateOrPushUndo()
    {
        var session = CreateSession();
        var tool = new ApplyVoxelPrimitivesMcpTool(
            new ApplyVoxelPrimitivesHandler(new VoxelPrimitiveGenerationService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(JsonArguments("""
        {
            "preview_only": true,
            "primitives": [
                {
                    "kind": "line",
                    "from": { "x": 0, "y": 0, "z": 0 },
                    "to": { "x": 2, "y": 0, "z": 0 },
                    "palette_index": 5
                }
            ]
        }
        """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("Preview generated 3 voxel assignment(s)", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, session.Document.Model.GetVoxelCount());
        Assert.False(session.UndoStack.CanUndo);
    }

    [Fact]
    public void ApplyVoxelPrimitivesMcpTool_ValidationErrorDoesNotApplyPartialMutation()
    {
        var session = CreateSession();
        var tool = new ApplyVoxelPrimitivesMcpTool(
            new ApplyVoxelPrimitivesHandler(new VoxelPrimitiveGenerationService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(JsonArguments("""
        {
            "primitives": [
                {
                    "kind": "block",
                    "at": { "x": 0, "y": 0, "z": 0 },
                    "palette_index": 2
                },
                {
                    "kind": "block",
                    "at": { "x": 1, "y": 0, "z": 0 },
                    "palette_index": 0
                }
            ]
        }
        """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("invalid palette index 0", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, session.Document.Model.GetVoxelCount());
        Assert.False(session.UndoStack.CanUndo);
    }

    [Fact]
    public void GetVoxelsInAreaMcpTool_ReturnsRegionLabels()
    {
        var session = CreateSession();
        var position = new Point3(2, 3, 4);
        var regionId = new RegionId("body");
        session.Document.Model.SetVoxel(position, 5);
        session.Document.Labels.AddOrUpdateRegion(new RegionDef
        {
            Id = regionId,
            Name = "Body",
        });
        session.Document.Labels.AssignRegion(regionId, [position]);

        var tool = new GetVoxelsInAreaMcpTool(
            new GetVoxelsInAreaHandler(new VoxelQueryService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        var result = tool.Invoke(JsonArguments("""
        { "min_x": 0, "min_y": 0, "min_z": 0, "max_x": 5, "max_y": 5, "max_z": 5 }
        """), CancellationToken.None);

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(result.Message);
        var voxel = document.RootElement.GetProperty("voxels")[0];
        Assert.Equal(2, voxel.GetProperty("x").GetInt32());
        Assert.Equal(5, voxel.GetProperty("i").GetInt32());
        Assert.Equal("body", voxel.GetProperty("regionId").GetString());
        Assert.Equal("Body", voxel.GetProperty("regionName").GetString());
        Assert.True(tool.IsReadOnly);
    }

    [Fact]
    public void ConsoleCountMcpTool_InvokesCommandWithoutRebuildingCommandLine()
    {
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);
        var tool = new ConsoleCountMcpTool(new CountCommand(new VoxelQueryService()), session);

        var result = tool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Total voxels: 1", result.Message);
    }

    [Fact]
    public void TypedConsoleMcpTools_EditAndQuerySessionState()
    {
        var session = CreateSession();
        var voxelEditingService = new VoxelEditingService();
        var queryService = new VoxelQueryService();
        var fillTool = new FillBoxMcpTool(new FillCommand(voxelEditingService), session);
        var getTool = new GetVoxelMcpTool(new GetVoxelCommand(queryService), session);
        var countTool = new CountVoxelsMcpTool(new CountCommand(queryService), session);
        var undoTool = new UndoMcpTool(new UndoCommand(), session);
        var redoTool = new RedoMcpTool(new RedoCommand(), session);
        var clearTool = new ClearModelMcpTool(new ClearCommand(voxelEditingService), session);

        var fillResult = fillTool.Invoke(JsonArguments("""
        { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 1, "z2": 1, "palette_index": 3 }
        """), CancellationToken.None);

        Assert.True(fillResult.Success);
        Assert.False(fillTool.IsReadOnly);
        Assert.Equal("Total voxels: 8", countTool.Invoke(EmptyArguments(), CancellationToken.None).Message);
        Assert.Equal("(1,1,1) = 3 (unknown)", getTool.Invoke(JsonArguments("""{ "x": 1, "y": 1, "z": 1 }"""), CancellationToken.None).Message);
        Assert.Equal("Voxels in region: 1", countTool.Invoke(JsonArguments("""
        { "box": { "x1": 1, "y1": 1, "z1": 1, "x2": 1, "y2": 1, "z2": 1 } }
        """), CancellationToken.None).Message);

        Assert.True(undoTool.Invoke(EmptyArguments(), CancellationToken.None).Success);
        Assert.Equal("Total voxels: 0", countTool.Invoke(EmptyArguments(), CancellationToken.None).Message);
        Assert.True(redoTool.Invoke(EmptyArguments(), CancellationToken.None).Success);
        Assert.Equal("Total voxels: 8", countTool.Invoke(EmptyArguments(), CancellationToken.None).Message);

        var clearResult = clearTool.Invoke(EmptyArguments(), CancellationToken.None);
        Assert.True(clearResult.Success);
        Assert.Equal("Total voxels: 0", countTool.Invoke(EmptyArguments(), CancellationToken.None).Message);
        Assert.False(clearTool.IsReadOnly);
        Assert.True(getTool.IsReadOnly);
        Assert.True(countTool.IsReadOnly);
    }

    [Fact]
    public void VisualTools_ReturnHeadlessLimitationError()
    {
        // The visual tools now require a capture service. When the service returns
        // failure (e.g. no Chromium available), the tool should report the error
        // rather than the old hardcoded headless message.
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures");
        var fakeService = new FakeViewerCaptureService(capturesDir, simulateFailure: true);
        var logger = NullLogger<ViewModelMcpTool>.Instance;
        var viewTool = new ViewModelMcpTool(fakeService, logger);

        var viewResult = viewTool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.False(viewResult.Success);
        Assert.Contains("Simulated capture failure", viewResult.Message, StringComparison.Ordinal);
        Assert.True(viewTool.IsReadOnly);

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public void ViewFromAngleMcpTool_AcceptsYawPitchArguments()
    {
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures");
        var fakeService = new FakeViewerCaptureService(capturesDir);
        var logger = NullLogger<ViewFromAngleMcpTool>.Instance;
        var angleTool = new ViewFromAngleMcpTool(fakeService, logger);

        Assert.Equal("object", angleTool.InputSchema.GetProperty("type").GetString());
        Assert.True(angleTool.IsReadOnly);

        // Test with yaw/pitch
        var result = angleTool.Invoke(
            JsonArguments("""{ "yaw": 1.5708, "pitch": 0.0, "preset": "right" }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("right", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captures", result.Message, StringComparison.OrdinalIgnoreCase);

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public void ViewModelMcpTool_ReturnsCaptureManifestOnSuccess()
    {
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures-capt");
        var fakeService = new FakeViewerCaptureService(capturesDir);
        var logger = NullLogger<ViewModelMcpTool>.Instance;
        var viewTool = new ViewModelMcpTool(fakeService, logger);

        var result = viewTool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("captures", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isometric", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Message.Length > 20);

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public void CaptureReferenceViewsMcpTool_DefaultPresetsIncludeStandardViews()
    {
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures-ref");
        var fakeService = new FakeViewerCaptureService(capturesDir);
        var logger = NullLogger<CaptureReferenceViewsMcpTool>.Instance;
        var tool = new CaptureReferenceViewsMcpTool(fakeService, logger);

        var result = tool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.True(result.Success, result.Message);

        // Parse manifest and verify all 4 default presets are included
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(4, doc.RootElement.GetProperty("capture_count").GetInt32());
        Assert.Equal(4, doc.RootElement.GetProperty("successful_count").GetInt32());

        var captures = doc.RootElement.GetProperty("captures").EnumerateArray().ToArray();
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "front");
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "right");
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "top");
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "isometric");

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public void CaptureReferenceViewsMcpTool_CustomPresetList()
    {
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures-custom");
        var fakeService = new FakeViewerCaptureService(capturesDir);
        var logger = NullLogger<CaptureReferenceViewsMcpTool>.Instance;
        var tool = new CaptureReferenceViewsMcpTool(fakeService, logger);

        var result = tool.Invoke(
            JsonArguments("""{ "presets": ["front", "back", "top"], "width": 640, "height": 480 }"""),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);

        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(3, doc.RootElement.GetProperty("capture_count").GetInt32());
        Assert.Equal(3, doc.RootElement.GetProperty("successful_count").GetInt32());

        var captures = doc.RootElement.GetProperty("captures").EnumerateArray().ToArray();
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "front");
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "back");
        Assert.Contains(captures, c => c.GetProperty("preset").GetString() == "top");
        Assert.DoesNotContain(captures, c => c.GetProperty("preset").GetString() == "isometric");

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task FakeViewerCaptureService_WritesRealPngFiles()
    {
        var capturesDir = Path.Combine(Path.GetTempPath(), "voxelforge-test-captures-png");
        var service = new FakeViewerCaptureService(capturesDir);

        var request = new ViewerCaptureRequest { Preset = "front", Label = "test-png" };
        var result = await service.CaptureAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ImagePath);
        Assert.True(File.Exists(result.ImagePath), $"File should exist at {result.ImagePath}");
        var fileInfo = new FileInfo(result.ImagePath);
        Assert.True(fileInfo.Length > 0, "PNG file should be non-empty");

        // Verify PNG header
        var header = new byte[8];
        using (var fs = File.OpenRead(result.ImagePath))
            fs.ReadExactly(header, 0, 8);
        Assert.Equal(0x89, header[0]);
        Assert.Equal(0x50, header[1]); // 'P'
        Assert.Equal(0x4E, header[2]); // 'N'
        Assert.Equal(0x47, header[3]); // 'G'

        // Clean up
        try { Directory.Delete(capturesDir, recursive: true); } catch { }
    }

    [Fact]
    public void ServerTool_ExposesProtocolMetadataFromAdapter()
    {
        var session = CreateSession();
        var tool = new DescribeModelMcpTool(
            new DescribeModelHandler(new VoxelQueryService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));
        var serverTool = new DescribeModelServerTool(tool);

        Assert.Equal("describe_model", serverTool.ProtocolTool.Name);
        Assert.Contains("voxel model", serverTool.ProtocolTool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("object", serverTool.ProtocolTool.InputSchema.GetProperty("type").GetString());
        Assert.True(serverTool.ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public void GetReferenceModelDiagnosticsMcpTool_ReturnsDiagnosticsForLoadedModel()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        session.ReferenceModels.Add(model);

        var tool = new GetReferenceModelDiagnosticsMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0 }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(0, doc.RootElement.GetProperty("index").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("raw_bounds", out _));
        Assert.True(doc.RootElement.TryGetProperty("world_bounds", out _));
        Assert.True(doc.RootElement.TryGetProperty("transform", out _));
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
        Assert.True(doc.RootElement.TryGetProperty("warnings", out _));
    }

    [Fact]
    public void GetReferenceModelDiagnosticsMcpTool_TransformedBoundsDifferFromRaw()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        model.Scale = 10f;
        model.PositionX = 5f;
        model.PositionY = 3f;
        model.PositionZ = -2f;
        session.ReferenceModels.Add(model);

        var tool = new GetReferenceModelDiagnosticsMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0 }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        var rawSize = doc.RootElement.GetProperty("raw_bounds").GetProperty("size");
        var worldSize = doc.RootElement.GetProperty("world_bounds").GetProperty("size");

        // Scaled by 10, so world size should be ~10x raw size
        double rawMax = Math.Max(Math.Max(rawSize.GetProperty("x").GetDouble(), rawSize.GetProperty("y").GetDouble()), rawSize.GetProperty("z").GetDouble());
        double worldMax = Math.Max(Math.Max(worldSize.GetProperty("x").GetDouble(), worldSize.GetProperty("y").GetDouble()), worldSize.GetProperty("z").GetDouble());
        Assert.True(worldMax > rawMax * 5, $"World max ({worldMax}) should be at least 5x raw max ({rawMax})");
    }

    [Fact]
    public void GetReferenceModelDiagnosticsMcpTool_MissingIndexReturnsFail()
    {
        var session = CreateSession();
        var tool = new GetReferenceModelDiagnosticsMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0 }"""),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model", result.Message);
    }

    [Fact]
    public void GetReferenceModelDiagnosticsMcpTool_IsReadOnly()
    {
        var tool = new GetReferenceModelDiagnosticsMcpTool(CreateSession());
        Assert.True(tool.IsReadOnly);
    }

    [Fact]
    public void SuggestReferenceTransformMcpTool_SuggestsScaleForTargetHeight()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        session.ReferenceModels.Add(model);

        var tool = new SuggestReferenceTransformMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "target_height": 10, "axis": "y" }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.True(doc.RootElement.GetProperty("suggested_scale").GetDouble() > 0);
        Assert.Equal(10, doc.RootElement.GetProperty("target_value").GetDouble());
        Assert.Equal("y", doc.RootElement.GetProperty("axis").GetString());
    }

    [Fact]
    public void SuggestReferenceTransformMcpTool_TargetMaxDimWorks()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        session.ReferenceModels.Add(model);

        var tool = new SuggestReferenceTransformMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "target_max_dim": 20 }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.True(doc.RootElement.GetProperty("suggested_scale").GetDouble() > 0);
        Assert.Equal(20, doc.RootElement.GetProperty("target_value").GetDouble());
    }

    [Fact]
    public void SuggestReferenceTransformMcpTool_UsesRotatedWorldAxisForTargetHeight()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        model.RotationX = 90f; // raw Z extent (3) becomes the world Y height basis.
        session.ReferenceModels.Add(model);

        var tool = new SuggestReferenceTransformMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "target_height": 6, "axis": "y" }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(2.0, doc.RootElement.GetProperty("suggested_scale").GetDouble(), precision: 3);
        Assert.Equal(3.0, doc.RootElement.GetProperty("current_unit_world_dimension").GetDouble(), precision: 3);
        Assert.Equal(6.0, doc.RootElement.GetProperty("expected_world_dimension_after_scale").GetDouble(), precision: 3);
    }

    [Fact]
    public void SuggestReferenceTransformMcpTool_IsReadOnly()
    {
        var tool = new SuggestReferenceTransformMcpTool(CreateSession());
        Assert.True(tool.IsReadOnly);
    }

    [Fact]
    public void FitReferenceModelMcpTool_AppliesScaleAndReportsBeforeAfter()
    {
        var session = CreateSession();
        var model = CreateTestModel();
        session.ReferenceModels.Add(model);

        var tool = new FitReferenceModelMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "target_height": 10, "axis": "y", "center": true }"""),
            CancellationToken.None);

        Assert.True(result.Success);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.True(doc.RootElement.TryGetProperty("before", out _));
        Assert.True(doc.RootElement.TryGetProperty("after", out _));

        var afterTransform = doc.RootElement.GetProperty("after").GetProperty("transform");
        Assert.True(afterTransform.GetProperty("scale").GetDouble() > 0);

        // Model should have been mutated
        Assert.True(model.Scale > 1f); // scaled up for target height of 10
        Assert.True(model.PositionY >= 0); // centered at Y=0
    }

    [Fact]
    public void FitReferenceModelMcpTool_NotReadOnly()
    {
        var tool = new FitReferenceModelMcpTool(CreateSession());
        Assert.False(tool.IsReadOnly);
    }

    [Fact]
    public void ReferenceModelDiagnostics_WarnsOnTinyExtent()
    {
        var model = CreateTinyModel();
        var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
        var warnings = ReferenceDiagnosticsHelper.ComputeWarnings(model, worldAabb);

        Assert.Contains(warnings, w => w.Code == "tiny_extent");
    }

    [Fact]
    public void ReferenceModelDiagnostics_NoWarningsOnReasonableModel()
    {
        var model = CreateTestModel();
        var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
        var warnings = ReferenceDiagnosticsHelper.ComputeWarnings(model, worldAabb);

        // Reasonably sized model should not have critical warnings
        Assert.DoesNotContain(warnings, w => w.Code == "tiny_extent");
        Assert.DoesNotContain(warnings, w => w.Code == "small_extent");
    }

    [Fact]
    public void ReferenceModelDiagnostics_NoColorVariationExplainsImporterVsViewerTextureLimit()
    {
        var model = CreatePlainModelWithoutTextureLinks();
        var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
        var warnings = ReferenceDiagnosticsHelper.ComputeWarnings(model, worldAabb);

        var warning = Assert.Single(warnings, w => w.Code == "no_color_variation");
        Assert.Contains("diagnostics show zero diffuse textures", warning.Message);
        Assert.Contains("importer did not link/bake", warning.Message);
        Assert.Contains("viewer is not ignoring a known texture path", warning.Message);
    }

    [Fact]
    public void ReferenceModelDiagnostics_TextureOnDiskWarnsViewerDoesNotRenderTextures()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-diagnostics-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "apple-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);
            var model = CreatePlainModelWithoutTextureLinks(texturePath);
            var worldAabb = ReferenceDiagnosticsHelper.ComputeTransformedAabb(model);
            var warnings = ReferenceDiagnosticsHelper.ComputeWarnings(model, worldAabb);

            var warning = Assert.Single(warnings, w => w.Code == "texture_available");
            Assert.Equal("info", warning.Severity);
            Assert.Contains("diffuse texture", warning.Message);
            Assert.Contains("THREE.TextureLoader", warning.Message);
            Assert.Contains("session-only", warning.Message);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private static ReferenceModelData CreateTestModel()
    {
        return new ReferenceModelData
        {
            FilePath = "/test/model.obj",
            Format = "obj",
            Meshes =
            [
                new ReferenceMeshData
                {
                    Vertices =
                    [
                        new ReferenceVertex(0, 0, 0, 0, 1, 0, 128, 128, 128, 255),
                        new ReferenceVertex(1, 0, 0, 0, 1, 0, 128, 128, 128, 255),
                        new ReferenceVertex(0, 2, 0, 0, 1, 0, 128, 128, 128, 255),
                        new ReferenceVertex(0, 0, 0, 0, 1, 0, 200, 100, 50, 255),
                        new ReferenceVertex(0, 0, 3, 0, 1, 0, 200, 100, 50, 255),
                        new ReferenceVertex(1, 1, 0, 0, 1, 0, 200, 100, 50, 255),
                    ],
                    Indices = [0, 1, 2, 3, 4, 5],
                    MaterialName = "test_mat",
                },
            ],
        };
    }

    private static ReferenceModelData CreatePlainModelWithoutTextureLinks(string? diffuseTexturePath = null)
    {
        return new ReferenceModelData
        {
            FilePath = "/test/plain-apple-like.glb",
            Format = "glb",
            Meshes =
            [
                new ReferenceMeshData
                {
                    Vertices =
                    [
                        new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255),
                        new ReferenceVertex(1, 0, 0, 0, 1, 0, 255, 255, 255, 255),
                        new ReferenceVertex(0, 2, 0, 0, 1, 0, 255, 255, 255, 255),
                    ],
                    Indices = [0, 1, 2],
                    MaterialName = "plain",
                    DiffuseTexturePath = diffuseTexturePath,
                },
            ],
        };
    }

    private static ReferenceModelData CreateTinyModel()
    {
        return new ReferenceModelData
        {
            FilePath = "/test/tiny.obj",
            Format = "obj",
            Meshes =
            [
                new ReferenceMeshData
                {
                    Vertices =
                    [
                        new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255),
                        new ReferenceVertex(0.001f, 0, 0, 0, 1, 0, 255, 255, 255, 255),
                        new ReferenceVertex(0, 0.002f, 0, 0, 1, 0, 255, 255, 255, 255),
                    ],
                    Indices = [0, 1, 2],
                    MaterialName = "tiny",
                },
            ],
        };
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }

    private static JsonElement EmptyArguments()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    private static JsonElement JsonArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
