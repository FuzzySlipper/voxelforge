using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core;
using VoxelForge.Core.LLM.Handlers;
using VoxelForge.Core.Reference;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Tools;
using VoxelForge.Mcp.Viewer;

namespace VoxelForge.Mcp.Tests;

public sealed class ReferenceModelMcpToolTests : IDisposable
{
    private readonly string _objPath;
    private readonly string _objWithUvsPath;

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

        _objWithUvsPath = Path.Combine(tempDir, "cube_uv.obj");
        File.WriteAllText(_objWithUvsPath, """
            o Cube
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            v 0 0 1
            v 1 0 1
            v 1 1 1
            v 0 1 1
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            f 1/1 2/2 3/3 4/4
            f 5/1 6/2 7/3 8/4
            f 1/1 2/2 6/4 5/3
            f 2/1 3/2 7/4 6/3
            f 3/1 4/2 8/4 7/3
            f 4/1 1/2 5/4 8/3
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
    public void LoadReferenceModel_UnityMatSidecarSetsBottomLeftSamplingDefaults()
    {
        var tempDir = Path.GetDirectoryName(_objPath)!;
        var unityObjPath = Path.Combine(tempDir, "unity_sidecar_quad.obj");
        var mtlPath = Path.Combine(tempDir, "unity_sidecar_quad.mtl");
        var matPath = Path.Combine(tempDir, "Golem.mat");

        File.WriteAllText(unityObjPath, """
            mtllib unity_sidecar_quad.mtl
            o Quad
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            usemtl Golem
            f 1/1 2/2 3/3 4/4
            """);
        File.WriteAllText(mtlPath, """
            newmtl Golem
            Kd 1 1 1
            """);
        File.WriteAllText(matPath, """
            %YAML 1.1
            --- !u!21 &2100000
            Material:
              m_Name: Golem
              m_SavedProperties:
                m_TexEnvs: []
                m_Floats: []
                m_Colors:
                - _Color: {r: 1, g: 1, b: 1, a: 1}
            """);

        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);

        var result = loadTool.Invoke(JsonArguments($"{{ \"path\": \"{unityObjPath}\" }}"), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var model = session.ReferenceModels.Get(0);
        Assert.NotNull(model);
        Assert.Equal("bottom_left", model.Meshes[0].UvOrigin);
        Assert.Equal("unity_sidecar", model.Meshes[0].SamplingControlsSource);

        var inspectTool = new InspectReferenceMaterialsMcpTool(session);
        var inspectResult = inspectTool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);
        Assert.True(inspectResult.Success, inspectResult.Message);
        using var doc = JsonDocument.Parse(inspectResult.Message);
        var mesh = doc.RootElement.GetProperty("meshes")[0];
        Assert.Equal("bottom_left", mesh.GetProperty("uv_origin").GetString());
        Assert.Equal("unity_sidecar", mesh.GetProperty("sampling_controls_source").GetString());
    }

    [Fact]
    public void LoadReferenceModel_VfReferenceSettingsSidecarOverridesTexturesAndSampling()
    {
        var tempDir = Path.GetDirectoryName(_objPath)!;
        var objPath = Path.Combine(tempDir, "settings_quad.obj");
        var mtlPath = Path.Combine(tempDir, "settings_quad.mtl");
        var settingsPath = Path.Combine(tempDir, "settings_quad.vf-reference-settings.json");
        var diffusePath = Path.Combine(tempDir, "settings_diffuse.png");
        var emissivePath = Path.Combine(tempDir, "settings_emissive.png");

        WriteTinyPng(diffusePath);
        WriteTinyPng(emissivePath);
        File.WriteAllText(objPath, """
            mtllib settings_quad.mtl
            o Quad
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            usemtl GOLEM_NORMAL_ROCK
            f 1/1 2/2 3/3 4/4
            """);
        File.WriteAllText(mtlPath, """
            newmtl GOLEM_NORMAL_ROCK
            Kd 1 1 1
            """);
        File.WriteAllText(settingsPath, """
            {
              "material": "GOLEM_NORMAL_ROCK",
              "sampling": {
                "uv_origin": "bottom_left",
                "flip_y": "asset_defined",
                "wrap_s": "clamp",
                "wrap_t": "clamp"
              },
              "textures": {
                "diffuse": "settings_diffuse.png",
                "emissive": "settings_emissive.png"
              },
              "emissive": { "suggestedBrightness": 0.75 }
            }
            """);

        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);

        var result = loadTool.Invoke(JsonArguments($"{{ \"path\": \"{objPath}\" }}"), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        var model = session.ReferenceModels.Get(0);
        Assert.NotNull(model);
        var mesh = model.Meshes[0];
        Assert.Equal(Path.GetFullPath(diffusePath), mesh.DiffuseTexturePath);
        Assert.Equal(Path.GetFullPath(emissivePath), mesh.EmissiveTexturePath);
        Assert.Equal("vf_reference_settings", mesh.DiffuseSourceLabel);
        Assert.Equal("vf_reference_settings", mesh.EmissiveSourceLabel);
        Assert.Equal("bottom_left", mesh.UvOrigin);
        Assert.Equal("asset_defined", mesh.FlipY);
        Assert.Equal("clamp", mesh.WrapS);
        Assert.Equal("clamp", mesh.WrapT);
        Assert.Equal("vf_reference_settings", mesh.SamplingControlsSource);
        Assert.Equal(0.75f, mesh.EmissiveBrightness);
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

    [Fact]
    public void InspectReferenceMaterials_Success()
    {
        var session = CreateSession();
        var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
        var loadTool = new LoadReferenceModelMcpTool(session, service);
        Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

        var inspectTool = new InspectReferenceMaterialsMcpTool(session);
        var result = inspectTool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        using var doc = JsonDocument.Parse(result.Message);
        Assert.Equal(0, doc.RootElement.GetProperty("model_index").GetInt32());
        Assert.Equal("cube.obj", doc.RootElement.GetProperty("file_name").GetString());
        Assert.Equal("OBJ", doc.RootElement.GetProperty("format").GetString());
        Assert.True(doc.RootElement.GetProperty("mesh_count").GetInt32() > 0);

        var meshes = doc.RootElement.GetProperty("meshes");
        Assert.True(meshes.GetArrayLength() > 0);
        var firstMesh = meshes[0];
        Assert.Equal(0, firstMesh.GetProperty("mesh_index").GetInt32());
        Assert.Equal("DefaultMaterial", firstMesh.GetProperty("material_name").GetString());
        Assert.Equal("none", firstMesh.GetProperty("diffuse_source_label").GetString());
        Assert.False(firstMesh.GetProperty("has_manual_override").GetBoolean());
    }

    [Fact]
    public void SetReferenceModelTexture_NoUvsFails()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-nouv-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

            // Verify the loaded mesh has no UVs (our simple OBJ has no vt lines)
            var model = session.ReferenceModels.Get(0);
            Assert.NotNull(model);
            Assert.False(model.Meshes[0].HasUvs);

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("no UV coordinates", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Null(model.Meshes[0].ManualDiffuseOverridePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_WithUvsSucceeds()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-uv-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            // Build an OBJ with explicit vt (texture coordinate) lines
            var uvObjPath = Path.Combine(Path.GetDirectoryName(_objPath)!, "cube_uv.obj");
            File.WriteAllText(uvObjPath, """
                o Cube
                v 0 0 0
                v 1 0 0
                v 1 1 0
                v 0 1 0
                v 0 0 1
                v 1 0 1
                v 1 1 1
                v 0 1 1
                vt 0 0
                vt 1 0
                vt 1 1
                vt 0 1
                f 1/1 2/2 3/3 4/4
                f 5/1 6/2 7/3 8/4
                f 1/1 2/2 6/4 5/3
                f 2/1 3/2 7/4 6/3
                f 3/1 4/2 8/4 7/3
                f 4/1 1/2 5/4 8/3
                """);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{uvObjPath}\" }}"), CancellationToken.None).Success);

            var model = session.ReferenceModels.Get(0);
            Assert.NotNull(model);
            Assert.True(model.Meshes[0].HasUvs);

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(texturePath, model.Meshes[0].ManualDiffuseOverridePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_Success()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-override-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objWithUvsPath}\" }}"), CancellationToken.None).Success);

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Contains("diffuse", result.Message, StringComparison.Ordinal);
            Assert.Contains("refsave", result.Message, StringComparison.Ordinal);

            // Verify the override was applied
            var model = session.ReferenceModels.Get(0);
            Assert.NotNull(model);
            Assert.Equal(texturePath, model.Meshes[0].ManualDiffuseOverridePath);
            Assert.Equal(texturePath, model.Meshes[0].EffectiveDiffuseTexturePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_InvalidPathFails()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-fail-");
        try
        {
            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

            var tool = new SetReferenceModelTextureMcpTool(session);

            // Test with non-existent path
            var missingPath = Path.Combine(tempDir.FullName, "missing.png");
            var failResult = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{missingPath}}" }
                """), CancellationToken.None);

            Assert.False(failResult.Success);
            Assert.Contains("not found", failResult.Message, StringComparison.OrdinalIgnoreCase);

            // Verify no mutation occurred (ManualDiffuseOverridePath should be null)
            var model = session.ReferenceModels.Get(0);
            Assert.NotNull(model);
            Assert.Null(model.Meshes[0].ManualDiffuseOverridePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_InvalidFormatFails()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-format-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test.unsupported");
            File.WriteAllBytes(texturePath, [0x00, 0x01, 0x02]);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objPath}\" }}"), CancellationToken.None).Success);

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("Unsupported", result.Message, StringComparison.Ordinal);

            // Verify no mutation occurred
            var model = session.ReferenceModels.Get(0);
            Assert.NotNull(model);
            Assert.Null(model.Meshes[0].ManualDiffuseOverridePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_ViewerRevisionIncrements()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-revision-");
        try
        {
            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objWithUvsPath}\" }}"), CancellationToken.None).Success);

            int preOverrideRevision = session.ViewerRevision;

            var texturePath = Path.Combine(tempDir.FullName, "test.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.True(result.Success, result.Message);

            // Viewer revision must have incremented after the override
            int postOverrideRevision = session.ViewerRevision;
            Assert.True(postOverrideRevision > preOverrideRevision,
                "Viewer revision should increment after manual texture override");
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_DiagnosticsReflectOverride()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-diag-override-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "diag-test.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objWithUvsPath}\" }}"), CancellationToken.None).Success);

            var setTool = new SetReferenceModelTextureMcpTool(session);
            Assert.True(setTool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None).Success);

            // Use inspect_reference_materials to see the override
            var inspectTool = new InspectReferenceMaterialsMcpTool(session);
            var inspectResult = inspectTool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);
            Assert.True(inspectResult.Success, inspectResult.Message);

            using var doc = JsonDocument.Parse(inspectResult.Message);
            var meshes = doc.RootElement.GetProperty("meshes");
            var firstMesh = meshes[0];

            // Must show manual override
            Assert.Equal("manual_override", firstMesh.GetProperty("diffuse_source_label").GetString());
            Assert.True(firstMesh.GetProperty("has_manual_override").GetBoolean());
            Assert.False(firstMesh.GetProperty("has_assimp_texture").GetBoolean());

            // The effective diffuse texture path should equal the override path
            var effectivePath = firstMesh.GetProperty("diffuse_texture_path").GetString();
            Assert.Equal(texturePath, effectivePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void MeshSnapshot_ManualTextureExposed()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-snapshot-tex-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "snapshot-test.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);

            var session = CreateSession();
            var service = new ReferenceAssetService(new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance));
            var loadTool = new LoadReferenceModelMcpTool(session, service);
            Assert.True(loadTool.Invoke(JsonArguments($"{{ \"path\": \"{_objWithUvsPath}\" }}"), CancellationToken.None).Success);

            var setTool = new SetReferenceModelTextureMcpTool(session);
            Assert.True(setTool.Invoke(JsonArguments($$"""
                { "index": 0, "mesh_index": 0, "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None).Success);

            // Build viewer data to verify texture exposure
            var viewerData = ViewerEndpointsTestAccessors.BuildReferenceModelDataListPublic(session.ReferenceModels.Models);
            Assert.NotEmpty(viewerData);

            var rm = viewerData[0];
            Assert.NotNull(rm.MeshTextures);
            Assert.NotEmpty(rm.MeshTextures);

            var meshTex = rm.MeshTextures[0];
            Assert.Equal(0, meshTex.MeshIndex);
            Assert.Equal("DefaultMaterial", meshTex.MaterialName);
            Assert.Equal(texturePath, meshTex.DiffuseTexturePath);
            Assert.Equal("manual_override", meshTex.DiffuseSourceLabel);
            Assert.Null(meshTex.NormalTexturePath);
            Assert.Null(meshTex.EmissiveTexturePath);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_MaterialName_NoUvsOnOneMeshFailsWithoutMutation()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-matname-nouv-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var session = CreateSession();

            // Build a model with 3 meshes:
            // - mesh 0: "SharedMat", has UVs
            // - mesh 1: "SharedMat", no UVs
            // - mesh 2: "OtherMat", has UVs
            var model = new ReferenceModelData
            {
                FilePath = "/fake/path/test.obj",
                Format = "OBJ",
                Meshes = new List<ReferenceMeshData>
                {
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                        Indices = [0],
                        MaterialName = "SharedMat",
                    },
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255)],
                        Indices = [0],
                        MaterialName = "SharedMat",
                    },
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.25f, 0.75f)],
                        Indices = [0],
                        MaterialName = "OtherMat",
                    },
                },
            };
            session.ReferenceModels.Add(model);

            int preRevision = session.ViewerRevision;

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "material_name": "SharedMat", "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("no UV coordinates", result.Message, StringComparison.OrdinalIgnoreCase);

            // No mesh should be mutated
            Assert.Null(model.Meshes[0].ManualDiffuseOverridePath);
            Assert.Null(model.Meshes[1].ManualDiffuseOverridePath);
            Assert.Null(model.Meshes[2].ManualDiffuseOverridePath);

            // No event should have been published (revision unchanged)
            Assert.Equal(preRevision, session.ViewerRevision);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_MaterialName_AllUvsSucceeds()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-matname-ok-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var session = CreateSession();

            var model = new ReferenceModelData
            {
                FilePath = "/fake/path/test.obj",
                Format = "OBJ",
                Meshes = new List<ReferenceMeshData>
                {
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                        Indices = [0],
                        MaterialName = "SharedMat",
                    },
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.25f, 0.75f)],
                        Indices = [0],
                        MaterialName = "SharedMat",
                    },
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.1f, 0.2f)],
                        Indices = [0],
                        MaterialName = "OtherMat",
                    },
                },
            };
            session.ReferenceModels.Add(model);

            int preRevision = session.ViewerRevision;

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "material_name": "SharedMat", "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.True(result.Success, result.Message);
            Assert.Equal(texturePath, model.Meshes[0].ManualDiffuseOverridePath);
            Assert.Equal(texturePath, model.Meshes[1].ManualDiffuseOverridePath);
            Assert.Null(model.Meshes[2].ManualDiffuseOverridePath);

            // Event should have been published (revision incremented)
            Assert.True(session.ViewerRevision > preRevision);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetReferenceModelTexture_MaterialName_NoMatchFailsWithoutMutation()
    {
        var tempDir = Directory.CreateTempSubdirectory("voxelforge-texture-matname-nomatch-");
        try
        {
            var texturePath = Path.Combine(tempDir.FullName, "test-diffuse.png");
            File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var session = CreateSession();

            var model = new ReferenceModelData
            {
                FilePath = "/fake/path/test.obj",
                Format = "OBJ",
                Meshes = new List<ReferenceMeshData>
                {
                    new()
                    {
                        Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                        Indices = [0],
                        MaterialName = "ExistingMat",
                    },
                },
            };
            session.ReferenceModels.Add(model);

            int preRevision = session.ViewerRevision;

            var tool = new SetReferenceModelTextureMcpTool(session);
            var result = tool.Invoke(JsonArguments($$"""
                { "index": 0, "material_name": "NonExistentMat", "slot": "diffuse", "path": "{{texturePath}}" }
                """), CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("No mesh found with material name matching 'NonExistentMat'", result.Message, StringComparison.Ordinal);

            // No mesh should be mutated
            Assert.Null(model.Meshes[0].ManualDiffuseOverridePath);

            // No event should have been published
            Assert.Equal(preRevision, session.ViewerRevision);
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    // ── Texture sampling control tests ──

    [Fact]
    public void SetReferenceTextureSampling_InvalidUvOriginFails()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "Mat",
                },
            },
        };
        session.ReferenceModels.Add(model);

        var tool = new SetReferenceTextureSamplingMcpTool(session);
        var result = tool.Invoke(JsonArguments("""{ "index": 0, "uv_origin": "invalid_value" }"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid uv_origin", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetReferenceTextureSampling_NoPropertiesFails()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "Mat",
                },
            },
        };
        session.ReferenceModels.Add(model);

        var tool = new SetReferenceTextureSamplingMcpTool(session);
        var result = tool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("At least one of", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SetReferenceTextureSampling_SetsUvOriginOnMeshAndRecordsProvenance()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "Mat",
                },
            },
        };
        session.ReferenceModels.Add(model);

        Assert.Equal("top_left", model.Meshes[0].UvOrigin);
        Assert.Equal("assimp", model.Meshes[0].SamplingControlsSource);

        var tool = new SetReferenceTextureSamplingMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "mesh_index": 0, "uv_origin": "bottom_left", "flip_y": "true", "wrap_s": "clamp", "wrap_t": "mirror" }"""),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("bottom_left", model.Meshes[0].UvOrigin);
        Assert.Equal("true", model.Meshes[0].FlipY);
        Assert.Equal("clamp", model.Meshes[0].WrapS);
        Assert.Equal("mirror", model.Meshes[0].WrapT);
        Assert.Equal("manual_sampling_override", model.Meshes[0].SamplingControlsSource);
    }

    [Fact]
    public void SetReferenceTextureSampling_ByMaterialNameAppliesToAllMatchingMeshes()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "MyMat",
                },
                new()
                {
                    Vertices = [new ReferenceVertex(1, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "MyMat",
                },
                new()
                {
                    Vertices = [new ReferenceVertex(2, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "OtherMat",
                },
            },
        };
        session.ReferenceModels.Add(model);

        var preRevision = session.ViewerRevision;

        var tool = new SetReferenceTextureSamplingMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "material_name": "MyMat", "uv_origin": "bottom_left" }"""),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("bottom_left", model.Meshes[0].UvOrigin);
        Assert.Equal("bottom_left", model.Meshes[1].UvOrigin);
        Assert.Equal("top_left", model.Meshes[2].UvOrigin); // unaffected
        Assert.Equal("manual_sampling_override", model.Meshes[0].SamplingControlsSource);
        Assert.True(session.ViewerRevision > preRevision); // event published
    }

    [Fact]
    public void SetReferenceTextureSampling_DefaultTargetsAllMeshes()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "A",
                },
                new()
                {
                    Vertices = [new ReferenceVertex(1, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "B",
                },
            },
        };
        session.ReferenceModels.Add(model);

        var tool = new SetReferenceTextureSamplingMcpTool(session);
        var result = tool.Invoke(
            JsonArguments("""{ "index": 0, "wrap_s": "clamp" }"""),
            CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal("clamp", model.Meshes[0].WrapS);
        Assert.Equal("clamp", model.Meshes[1].WrapS);
    }

    [Fact]
    public void InspectReferenceMaterials_ReportsSamplingControls()
    {
        var session = CreateSession();
        var model = new ReferenceModelData
        {
            FilePath = "/fake/path/test.obj",
            Format = "OBJ",
            Meshes = new List<ReferenceMeshData>
            {
                new()
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255, 0.5f, 0.5f)],
                    Indices = [0],
                    MaterialName = "Mat",
                },
            },
        };
        session.ReferenceModels.Add(model);

        // Set sampling override
        model.Meshes[0].UvOrigin = "bottom_left";
        model.Meshes[0].FlipY = "true";
        model.Meshes[0].WrapS = "clamp";
        model.Meshes[0].WrapT = "mirror";
        model.Meshes[0].SamplingControlsSource = "manual_sampling_override";

        var inspectTool = new InspectReferenceMaterialsMcpTool(session);
        var result = inspectTool.Invoke(JsonArguments("""{ "index": 0 }"""), CancellationToken.None);

        Assert.True(result.Success, result.Message);
        using var doc = JsonDocument.Parse(result.Message);
        var meshes = doc.RootElement.GetProperty("meshes");
        Assert.True(meshes.GetArrayLength() > 0);
        var firstMesh = meshes[0];
        Assert.Equal("bottom_left", firstMesh.GetProperty("uv_origin").GetString());
        Assert.Equal("true", firstMesh.GetProperty("flip_y").GetString());
        Assert.Equal("clamp", firstMesh.GetProperty("wrap_s").GetString());
        Assert.Equal("mirror", firstMesh.GetProperty("wrap_t").GetString());
        Assert.Equal("manual_sampling_override", firstMesh.GetProperty("sampling_controls_source").GetString());
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

    private static void WriteTinyPng(string path)
    {
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));
    }
}
