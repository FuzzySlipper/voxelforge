using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using VoxelForge.App;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class McpToolTests
{
    [Fact]
    public void ToolRegistry_RegistersMcpToolsExplicitly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(new EditorConfigState());
        services.AddSingleton(new VoxelForgeMcpOptions());
        services.AddSingleton<VoxelForgeMcpSession>();
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
                "check_collision",
                "clear_model",
                "compare_reference",
                "console_count",
                "count_voxels",
                "create_region",
                "delete_region",
                "describe_model",
                "fill_box",
                "get_cross_section",
                "get_interface_voxels",
                "get_model_info",
                "get_region_bounds",
                "get_region_neighbors",
                "get_region_tree",
                "get_region_voxels",
                "get_voxel",
                "get_voxels_in_area",
                "list_models",
                "list_palette",
                "list_regions",
                "load_model",
                "measure_distance",
                "new_model",
                "publish_preview",
                "redo",
                "remove_voxels",
                "save_model",
                "set_grid_hint",
                "set_palette_entry",
                "set_voxels",
                "undo",
                "view_from_angle",
                "view_model",
            ],
            toolNames);
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
        var viewTool = new ViewModelMcpTool();
        var angleTool = new ViewFromAngleMcpTool();
        var compareTool = new CompareReferenceMcpTool();

        var viewResult = viewTool.Invoke(EmptyArguments(), CancellationToken.None);
        var angleResult = angleTool.Invoke(JsonArguments("""{ "yaw": 0.0, "pitch": 0.0 }"""), CancellationToken.None);
        var compareResult = compareTool.Invoke(EmptyArguments(), CancellationToken.None);

        Assert.False(viewResult.Success);
        Assert.False(angleResult.Success);
        Assert.False(compareResult.Success);
        Assert.Contains("headless", viewResult.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FNA renderer", viewResult.Message, StringComparison.Ordinal);
        Assert.Equal("object", angleTool.InputSchema.GetProperty("type").GetString());
        Assert.True(viewTool.IsReadOnly);
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
