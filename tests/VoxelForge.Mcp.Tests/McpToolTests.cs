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
        services.AddSingleton<VoxelForgeMcpSession>();
        services.AddVoxelForgeMcpTools();

        using var provider = services.BuildServiceProvider();

        var toolNames = provider.GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["console_count", "describe_model"], toolNames);
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

        var result = tool.Invoke(EmptyArguments());

        Assert.True(result.Success);
        Assert.Contains("Voxel model with 1 voxels", result.Message, StringComparison.Ordinal);
        Assert.Contains("test", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleCountMcpTool_InvokesCommandWithoutRebuildingCommandLine()
    {
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);
        var tool = new ConsoleCountMcpTool(new CountCommand(new VoxelQueryService()), session);

        var result = tool.Invoke(EmptyArguments());

        Assert.True(result.Success);
        Assert.Equal("Total voxels: 1", result.Message);
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
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }

    private static JsonElement EmptyArguments()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }
}
