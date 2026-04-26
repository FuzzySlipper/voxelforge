using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class ModelLifecycleMcpToolTests
{
    [Fact]
    public void ModelLifecycleTools_CreatePaletteSaveLoadAndListModels()
    {
        var projectDirectory = CreateTempProjectDirectory();
        try
        {
            var options = new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory };
            var pathResolver = new ModelPathResolver(options);
            var session = CreateSession();
            var lifecycleService = new ProjectLifecycleService(NullLoggerFactory.Instance);
            var paletteService = new PaletteMaterialService();
            var voxelEditingService = new VoxelEditingService();
            var newModel = new NewModelMcpTool(session, NullLoggerFactory.Instance, new EditorConfigState());
            var setPalette = new SetPaletteEntryMcpTool(session, paletteService);
            var listPalette = new ListPaletteMcpTool(session, paletteService);
            var setGrid = new SetGridHintMcpTool(session, voxelEditingService);
            var setVoxels = new SetVoxelsMcpTool(
                new SetVoxelsHandler(new VoxelMutationIntentService()),
                session,
                new LlmToolApplicationService(voxelEditingService));
            var saveModel = new SaveModelMcpTool(session, lifecycleService, pathResolver);
            var loadModel = new LoadModelMcpTool(session, lifecycleService, pathResolver);
            var listModels = new ListModelsMcpTool(session, pathResolver);
            var infoTool = new GetModelInfoMcpTool(
                new GetModelInfoHandler(new VoxelQueryService()),
                session,
                new LlmToolApplicationService(voxelEditingService));

            var newResult = newModel.Invoke(JsonArguments("""
            {
                "name": "robot",
                "grid_hint": 48,
                "palette_entries": [
                    { "index": 1, "name": "aluminum", "r": 180, "g": 190, "b": 200 },
                    { "index": 2, "name": "plastic", "r": 20, "g": 30, "b": 40, "a": 220 }
                ]
            }
            """), CancellationToken.None);
            Assert.True(newResult.Success);
            Assert.Equal(48, session.Document.Model.GridHint);
            Assert.Equal("aluminum", session.Document.Model.Palette.Get(1)?.Name);

            var paletteResult = setPalette.Invoke(JsonArguments("""
            { "index": 3, "name": "steel_strut", "r": 90, "g": 91, "b": 92, "a": 255 }
            """), CancellationToken.None);
            Assert.True(paletteResult.Success);
            Assert.True(setGrid.Invoke(JsonArguments("""{ "size": 64 }"""), CancellationToken.None).Success);
            Assert.Equal(64, session.Document.Model.GridHint);

            Assert.True(setVoxels.Invoke(JsonArguments("""
            { "voxels": [ { "x": 1, "y": 2, "z": 3, "i": 3 } ] }
            """), CancellationToken.None).Success);

            using (var paletteDocument = JsonDocument.Parse(listPalette.Invoke(EmptyArguments(), CancellationToken.None).Message))
            {
                Assert.Equal(3, paletteDocument.RootElement.GetProperty("count").GetInt32());
                Assert.Equal("steel_strut", paletteDocument.RootElement.GetProperty("entries")[2].GetProperty("name").GetString());
            }

            Assert.True(saveModel.Invoke(EmptyArguments(), CancellationToken.None).Success);
            Assert.True(File.Exists(Path.Combine(projectDirectory, "robot.vforge")));

            Assert.True(newModel.Invoke(JsonArguments("""{ "name": "blank", "grid_hint": 16 }"""), CancellationToken.None).Success);
            Assert.Equal(0, session.Document.Model.GetVoxelCount());
            var loadResult = loadModel.Invoke(JsonArguments("""{ "name": "robot" }"""), CancellationToken.None);
            Assert.True(loadResult.Success);

            Assert.Equal((byte)3, session.Document.Model.GetVoxel(new Point3(1, 2, 3)));
            Assert.Equal("steel_strut", session.Document.Model.Palette.Get(3)?.Name);
            Assert.Equal(64, session.Document.Model.GridHint);

            using (var modelsDocument = JsonDocument.Parse(listModels.Invoke(EmptyArguments(), CancellationToken.None).Message))
            {
                Assert.Equal(1, modelsDocument.RootElement.GetProperty("count").GetInt32());
                Assert.Equal("robot", modelsDocument.RootElement.GetProperty("models")[0].GetProperty("name").GetString());
            }

            using (var infoDocument = JsonDocument.Parse(infoTool.Invoke(EmptyArguments(), CancellationToken.None).Message))
            {
                Assert.Equal(1, infoDocument.RootElement.GetProperty("voxelCount").GetInt32());
                Assert.Equal(3, infoDocument.RootElement.GetProperty("paletteEntries").GetArrayLength());
            }
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
                Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public void ModelLifecycleTools_RejectUnsafeModelNamesAndReservedPaletteIndex()
    {
        var projectDirectory = CreateTempProjectDirectory();
        try
        {
            var session = CreateSession();
            var pathResolver = new ModelPathResolver(new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory });
            var saveModel = new SaveModelMcpTool(session, new ProjectLifecycleService(NullLoggerFactory.Instance), pathResolver);
            var setPalette = new SetPaletteEntryMcpTool(session, new PaletteMaterialService());

            var saveResult = saveModel.Invoke(JsonArguments("""{ "name": "../escape" }"""), CancellationToken.None);
            var paletteResult = setPalette.Invoke(JsonArguments("""
            { "index": 0, "name": "air", "r": 0, "g": 0, "b": 0 }
            """), CancellationToken.None);

            Assert.False(saveResult.Success);
            Assert.Contains("configured project directory", saveResult.Message, StringComparison.Ordinal);
            Assert.False(paletteResult.Success);
            Assert.Contains("reserved for air", paletteResult.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
                Directory.Delete(projectDirectory, recursive: true);
        }
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }

    private static string CreateTempProjectDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "voxelforge-mcp-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
