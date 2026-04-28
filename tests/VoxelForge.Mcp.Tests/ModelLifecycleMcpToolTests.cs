using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Serialization;
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
    public void PublishPreview_WritesAtomicSnapshotAndManifestWithoutChangingSaveModelBehavior()
    {
        var projectDirectory = CreateTempProjectDirectory();
        try
        {
            var session = CreateSession();
            var pathResolver = new ModelPathResolver(new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory });
            var newModel = new NewModelMcpTool(session, NullLoggerFactory.Instance, new EditorConfigState());
            var publishPreview = new PublishPreviewMcpTool(session, pathResolver, NullLoggerFactory.Instance);
            var saveModel = new SaveModelMcpTool(
                session,
                new ProjectLifecycleService(NullLoggerFactory.Instance),
                pathResolver);
            Assert.True(newModel.Invoke(JsonArguments("""
            {
                "name": "agent-session",
                "grid_hint": 24,
                "palette_entries": [
                    { "index": 1, "name": "preview-blue", "r": 10, "g": 20, "b": 240 }
                ]
            }
            """), CancellationToken.None).Success);
            session.Document.Model.SetVoxel(new Point3(2, 3, 4), 1);

            var result = publishPreview.Invoke(JsonArguments("""{ "name": "live-preview" }"""), CancellationToken.None);

            Assert.True(result.Success);
            string previewPath = Path.Combine(projectDirectory, "live-preview.vforge");
            string manifestPath = Path.Combine(projectDirectory, "live-preview.preview.json");
            Assert.True(File.Exists(previewPath));
            Assert.True(File.Exists(manifestPath));
            Assert.False(Directory.EnumerateFiles(projectDirectory, "*.tmp", SearchOption.TopDirectoryOnly).Any());
            Assert.False(File.Exists(Path.Combine(projectDirectory, "agent-session.vforge")));

            var serializer = new ProjectSerializer(NullLoggerFactory.Instance);
            var (model, _, _, meta) = serializer.Deserialize(File.ReadAllText(previewPath));
            Assert.Equal("agent-session", meta.Name);
            Assert.Equal((byte)1, model.GetVoxel(new Point3(2, 3, 4)));

            using (var resultDocument = JsonDocument.Parse(result.Message))
            {
                Assert.Equal(previewPath, resultDocument.RootElement.GetProperty("path").GetString());
                Assert.Equal(manifestPath, resultDocument.RootElement.GetProperty("manifest_path").GetString());
                Assert.Equal(1, resultDocument.RootElement.GetProperty("voxel_count").GetInt32());
            }

            using (var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath)))
            {
                Assert.Equal("voxelforge.preview_manifest", manifestDocument.RootElement.GetProperty("schema").GetString());
                Assert.Equal("agent-session", manifestDocument.RootElement.GetProperty("model_name").GetString());
                Assert.Equal(previewPath, manifestDocument.RootElement.GetProperty("model_path").GetString());
                Assert.Equal("live-preview.vforge", manifestDocument.RootElement.GetProperty("model_file").GetString());
                Assert.Equal(1, manifestDocument.RootElement.GetProperty("voxel_count").GetInt32());
            }

            Assert.True(saveModel.Invoke(EmptyArguments(), CancellationToken.None).Success);
            Assert.True(File.Exists(Path.Combine(projectDirectory, "agent-session.vforge")));
        }
        finally
        {
            if (Directory.Exists(projectDirectory))
                Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public void PublishPreview_CanSkipManifestAndRejectsUnsafeNames()
    {
        var projectDirectory = CreateTempProjectDirectory();
        try
        {
            var session = CreateSession();
            var tool = new PublishPreviewMcpTool(
                session,
                new ModelPathResolver(new VoxelForgeMcpOptions { ProjectDirectory = projectDirectory }),
                NullLoggerFactory.Instance);

            var skipResult = tool.Invoke(JsonArguments("""{ "name": "no-manifest", "write_manifest": false }"""), CancellationToken.None);
            var unsafeResult = tool.Invoke(JsonArguments("""{ "name": "../escape" }"""), CancellationToken.None);

            Assert.True(skipResult.Success);
            Assert.True(File.Exists(Path.Combine(projectDirectory, "no-manifest.vforge")));
            Assert.False(File.Exists(Path.Combine(projectDirectory, "no-manifest.preview.json")));
            Assert.False(unsafeResult.Success);
            Assert.Contains("configured project directory", unsafeResult.Message, StringComparison.Ordinal);
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
