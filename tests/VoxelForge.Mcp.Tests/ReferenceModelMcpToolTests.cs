using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class ReferenceModelMcpToolTests : IDisposable
{
    private readonly string _objPath;

    public ReferenceModelMcpToolTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-refmodel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _objPath = Path.Combine(tempDir, "cube.obj");
        File.WriteAllText(_objPath, """
            o Cube
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            v 0 0 1
            v 1 0 1
            v 1 1 1
            v 0 1 1
            f 1 2 3 4
            f 5 6 7 8
            f 1 2 6 5
            f 2 3 7 6
            f 3 4 8 7
            f 4 1 5 8
            """);
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(_objPath);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void LoadReferenceModel_Success()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var tool = new LoadReferenceModelMcpTool(session, service);

        var result = tool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("Loaded [0]", result.Message, StringComparison.Ordinal);
        Assert.Single(session.ReferenceModels.Models);
    }

    [Fact]
    public void LoadReferenceModel_MissingPathFails()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var tool = new LoadReferenceModelMcpTool(session, service);

        var result = tool.Invoke(JsonArguments("""{ "path": "" }"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("cannot be empty", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadReferenceModel_MissingFileFails()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var tool = new LoadReferenceModelMcpTool(session, service);

        var missingPath = Path.Combine(Path.GetDirectoryName(_objPath)!, "missing.obj");
        var result = tool.Invoke(JsonArguments($"{{ \"path\": \"{missingPath}\" }}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListReferenceModels_EmptyAndPopulated()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var listTool = new ListReferenceModelsMcpTool(session, service);

        var emptyResult = listTool.Invoke(EmptyArguments(), CancellationToken.None);
        Assert.True(emptyResult.Success);
        using (var doc = JsonDocument.Parse(emptyResult.Message))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("count").GetInt32());
        }

        var loadTool = new LoadReferenceModelMcpTool(session, service);
        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var populatedResult = listTool.Invoke(EmptyArguments(), CancellationToken.None);
        Assert.True(populatedResult.Success);
        using (var doc = JsonDocument.Parse(populatedResult.Message))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("count").GetInt32());
            var model = doc.RootElement.GetProperty("models")[0];
            Assert.Equal(0, model.GetProperty("index").GetInt32());
            Assert.Equal("cube.obj", model.GetProperty("file_name").GetString());
            Assert.Equal("OBJ", model.GetProperty("format").GetString());
        }
    }

    [Fact]
    public void TransformReferenceModel_SuccessAndQuery()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var transformTool = new TransformReferenceModelMcpTool(session);

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var result = transformTool.Invoke(JsonArguments("""
            { "index": 0, "x": 10, "y": 20, "z": 30, "rx": 45, "ry": 90, "scale": 2 }
            """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("pos=(10,20,30)", result.Message, StringComparison.Ordinal);

        var model = session.ReferenceModels.Get(0);
        Assert.NotNull(model);
        Assert.Equal(10f, model.PositionX);
        Assert.Equal(20f, model.PositionY);
        Assert.Equal(30f, model.PositionZ);
        Assert.Equal(45f, model.RotationX);
        Assert.Equal(90f, model.RotationY);
        Assert.Equal(0f, model.RotationZ);
        Assert.Equal(2f, model.Scale);
    }

    [Fact]
    public void TransformReferenceModel_BadIndexFails()
    {
        var session = CreateSession();
        var transformTool = new TransformReferenceModelMcpTool(session);

        var result = transformTool.Invoke(JsonArguments("""
            { "index": 0, "x": 0, "y": 0, "z": 0 }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model at index", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveReferenceModel_SuccessAndBadIndex()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var removeTool = new RemoveReferenceModelMcpTool(session, service);

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);
        Assert.Single(session.ReferenceModels.Models);

        var badResult = removeTool.Invoke(JsonArguments("""{ "index": 5 }"""), CancellationToken.None);
        Assert.False(badResult.Success);
        Assert.Contains("No reference model at index", badResult.Message, StringComparison.Ordinal);

        var goodResult = removeTool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);
        Assert.True(goodResult.Success);
        Assert.Empty(session.ReferenceModels.Models);
    }

    [Fact]
    public void ClearReferenceModels_RemovesAll()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var clearTool = new ClearReferenceModelsMcpTool(session, service);

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);
        Assert.Single(session.ReferenceModels.Models);

        var result = clearTool.Invoke(EmptyArguments(), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Empty(session.ReferenceModels.Models);
    }

    [Fact]
    public void VoxelizeReferenceModel_SuccessAndReflectsInModelInfo()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var voxelizeTool = new VoxelizeReferenceModelMcpTool(
            new VoxelizeCommand(session.ReferenceModels, NullLoggerFactory.Instance),
            session);
        var infoTool = new GetModelInfoMcpTool(
            new GetModelInfoHandler(new VoxelQueryService()),
            session,
            new LlmToolApplicationService(new VoxelEditingService()));

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var result = voxelizeTool.Invoke(JsonArguments("""
            { "index": 0, "resolution": 8, "mode": "solid" }
            """), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Contains("voxels", result.Message, StringComparison.Ordinal);

        var infoResult = infoTool.Invoke(EmptyArguments(), CancellationToken.None);
        Assert.True(infoResult.Success);
        using var doc = JsonDocument.Parse(infoResult.Message);
        Assert.True(doc.RootElement.GetProperty("voxelCount").GetInt32() > 0);
    }

    [Fact]
    public void VoxelizeReferenceModel_BadIndexFails()
    {
        var session = CreateSession();
        var voxelizeTool = new VoxelizeReferenceModelMcpTool(
            new VoxelizeCommand(session.ReferenceModels, NullLoggerFactory.Instance),
            session);

        var result = voxelizeTool.Invoke(JsonArguments("""
            { "index": 0, "resolution": 8 }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference model at index", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(257)]
    public void VoxelizeReferenceModel_ResolutionOutOfRangeFails(int resolution)
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var voxelizeTool = new VoxelizeReferenceModelMcpTool(
            new VoxelizeCommand(session.ReferenceModels, NullLoggerFactory.Instance),
            session);

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var result = voxelizeTool.Invoke(JsonArguments($"{{ \"index\": 0, \"resolution\": {resolution} }}"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("between 2 and 256", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void VoxelizeReferenceModel_BadModeFails()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        var voxelizeTool = new VoxelizeReferenceModelMcpTool(
            new VoxelizeCommand(session.ReferenceModels, NullLoggerFactory.Instance),
            session);

        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var result = voxelizeTool.Invoke(JsonArguments("""
            { "index": 0, "resolution": 8, "mode": "wireframe" }
            """), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("'solid' or 'surface'", result.Message, StringComparison.Ordinal);
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
