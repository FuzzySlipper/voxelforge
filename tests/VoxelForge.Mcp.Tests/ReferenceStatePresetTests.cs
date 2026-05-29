using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core.Reference;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class ReferenceStatePresetTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _objPath;
    private readonly string _objWithUvsPath;

    public ReferenceStatePresetTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-preset-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _objPath = Path.Combine(_tempDir, "cube.obj");
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

        _objWithUvsPath = Path.Combine(_tempDir, "cube_uv.obj");
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
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void FromModels_EmptyState_CreatesEmptyPreset()
    {
        var models = new List<ReferenceModelData>();
        var preset = ReferenceStatePreset.FromModels(models);

        Assert.NotNull(preset);
        Assert.Equal(ReferenceStatePreset.CurrentSchemaVersion, preset.SchemaVersion);
        Assert.Empty(preset.Entries);
        Assert.NotNull(preset.ToJson());
    }

    [Fact]
    public void FromModels_SingleModel_CapturesFilePathAndDefaults()
    {
        var model = CreateSimpleModel("my_model.fbx");
        var preset = ReferenceStatePreset.FromModels([model]);

        Assert.Single(preset.Entries);
        var entry = preset.Entries[0];
        Assert.Equal("/path/to/my_model.fbx", entry.SourcePath);
        Assert.Equal("FBX", entry.Format);
        Assert.Equal(0f, entry.PositionX);
        Assert.Equal(0f, entry.PositionY);
        Assert.Equal(0f, entry.PositionZ);
        Assert.Equal(1f, entry.Scale);
        Assert.True(entry.IsVisible);
        Assert.Equal("solid", entry.RenderMode);
    }

    [Fact]
    public void FromModels_WithTransform_CapturesTransform()
    {
        var model = CreateSimpleModel("test.obj");
        model.PositionX = 1.5f;
        model.PositionY = 2.5f;
        model.PositionZ = -3f;
        model.RotationX = 45f;
        model.RotationY = 90f;
        model.RotationZ = 180f;
        model.Scale = 2f;

        var preset = ReferenceStatePreset.FromModels([model]);
        var entry = preset.Entries[0];

        Assert.Equal(1.5f, entry.PositionX);
        Assert.Equal(2.5f, entry.PositionY);
        Assert.Equal(-3f, entry.PositionZ);
        Assert.Equal(45f, entry.RotationX);
        Assert.Equal(90f, entry.RotationY);
        Assert.Equal(180f, entry.RotationZ);
        Assert.Equal(2f, entry.Scale);
    }

    [Fact]
    public void FromModels_WithVisibilityAndRenderMode_CapturesCorrectly()
    {
        var model = CreateSimpleModel("test.obj");
        model.IsVisible = false;
        model.RenderMode = ReferenceRenderMode.Wireframe;

        var preset = ReferenceStatePreset.FromModels([model]);
        var entry = preset.Entries[0];

        Assert.False(entry.IsVisible);
        Assert.Equal("wireframe", entry.RenderMode);
    }

    [Fact]
    public void FromModels_WithManualTextureOverrides_CapturesOverrides()
    {
        var model = CreateSimpleModel("test.fbx");
        var mesh = model.Meshes[0];
        mesh.ManualDiffuseOverridePath = "/textures/diffuse.png";
        mesh.ManualNormalOverridePath = "/textures/normal.png";
        mesh.ManualEmissiveOverridePath = "/textures/emissive.png";

        var preset = ReferenceStatePreset.FromModels([model]);
        var entry = preset.Entries[0];

        Assert.NotNull(entry.MeshOverrides);
        var ov = Assert.Single(entry.MeshOverrides);
        Assert.Equal(0, ov.MeshIndex);
        Assert.Equal("/textures/diffuse.png", ov.ManualDiffuseOverridePath);
        Assert.Equal("/textures/normal.png", ov.ManualNormalOverridePath);
        Assert.Equal("/textures/emissive.png", ov.ManualEmissiveOverridePath);
    }

    [Fact]
    public void FromModels_WithSamplingOverrides_CapturesNonDefaults()
    {
        var model = CreateSimpleModel("test.fbx");
        var mesh = model.Meshes[0];
        mesh.UvOrigin = "bottom_left";
        mesh.FlipY = "true";
        mesh.WrapS = "clamp";
        mesh.WrapT = "mirror";
        mesh.SamplingControlsSource = "manual_sampling_override";

        var preset = ReferenceStatePreset.FromModels([model]);
        var entry = preset.Entries[0];

        Assert.NotNull(entry.MeshOverrides);
        var ov = Assert.Single(entry.MeshOverrides);
        Assert.Equal("bottom_left", ov.UvOrigin);
        Assert.Equal("true", ov.FlipY);
        Assert.Equal("clamp", ov.WrapS);
        Assert.Equal("mirror", ov.WrapT);
        Assert.Equal("manual_sampling_override", ov.SamplingControlsSource);
    }

    [Fact]
    public void FromModels_WithDefaultSampling_OmittedFromOverrides()
    {
        var model = CreateSimpleModel("test.fbx");
        // Defaults: top_left, asset_defined, repeat, repeat, assimp

        var preset = ReferenceStatePreset.FromModels([model]);
        var entry = preset.Entries[0];

        Assert.Null(entry.MeshOverrides); // No non-default values
    }

    [Fact]
    public void FromModels_WithLabelAndNotes_StoredInEnvelope()
    {
        var model = CreateSimpleModel("test.fbx");
        var preset = ReferenceStatePreset.FromModels(
            [model],
            label: "AM Golem Setup",
            notes: "Matched UV origin and texture overrides for AM Golem FBX.",
            createdBy: "test-agent");

        Assert.Equal("AM Golem Setup", preset.Label);
        Assert.Equal("Matched UV origin and texture overrides for AM Golem FBX.", preset.Notes);
        Assert.Equal("test-agent", preset.CreatedBy);
        Assert.NotNull(preset.CreatedAt);
    }

    [Fact]
    public void RoundTrip_SerializeAndDeserialize_PreservesAllFields()
    {
        // Build a model with transform, texture overrides, and sampling
        var model = CreateSimpleModel("test.obj");
        model.PositionX = 10;
        model.PositionY = 20;
        model.PositionZ = 30;
        model.RotationX = 15;
        model.RotationY = 30;
        model.RotationZ = 45;
        model.Scale = 2.5f;
        model.IsVisible = false;
        model.RenderMode = ReferenceRenderMode.Transparent;

        var mesh = model.Meshes[0];
        mesh.ManualDiffuseOverridePath = "/tmp/textures/my_diffuse.png";
        mesh.ManualNormalOverridePath = "/tmp/textures/my_normal.png";
        mesh.UvOrigin = "bottom_left";
        mesh.FlipY = "true";
        mesh.WrapS = "clamp";
        mesh.WrapT = "clamp";
        mesh.SamplingControlsSource = "manual_sampling_override";

        // Export to JSON
        var preset = ReferenceStatePreset.FromModels([model], label: "RoundTrip Test");
        var json = preset.ToJson();

        // Verify schema version validates
        Assert.True(ReferenceStatePreset.TryValidateSchema(json, out var schemaError));
        Assert.Null(schemaError);

        // Deserialize
        var restored = ReferenceStatePreset.FromJson(json);
        Assert.NotNull(restored);
        Assert.Equal("RoundTrip Test", restored.Label);
        Assert.Single(restored.Entries);

        var entry = restored.Entries[0];
        Assert.Equal("/path/to/test.obj", entry.SourcePath);
        Assert.Equal(10f, entry.PositionX);
        Assert.Equal(20f, entry.PositionY);
        Assert.Equal(30f, entry.PositionZ);
        Assert.Equal(15f, entry.RotationX);
        Assert.Equal(30f, entry.RotationY);
        Assert.Equal(45f, entry.RotationZ);
        Assert.Equal(2.5f, entry.Scale);
        Assert.False(entry.IsVisible);
        Assert.Equal("transparent", entry.RenderMode);

        Assert.NotNull(entry.MeshOverrides);
        var ov = Assert.Single(entry.MeshOverrides);
        Assert.Equal("/tmp/textures/my_diffuse.png", ov.ManualDiffuseOverridePath);
        Assert.Equal("/tmp/textures/my_normal.png", ov.ManualNormalOverridePath);
        Assert.Equal("bottom_left", ov.UvOrigin);
        Assert.Equal("true", ov.FlipY);
        Assert.Equal("clamp", ov.WrapS);
        Assert.Equal("clamp", ov.WrapT);
        Assert.Equal("manual_sampling_override", ov.SamplingControlsSource);
    }

    [Fact]
    public void SchemaValidation_RejectsWrongVersion()
    {
        var json = """{ "schemaVersion": 999, "entries": [] }""";
        Assert.False(ReferenceStatePreset.TryValidateSchema(json, out var error));
        Assert.Contains("999", error, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaValidation_RejectsMissingVersion()
    {
        var json = """{ "entries": [] }""";
        Assert.False(ReferenceStatePreset.TryValidateSchema(json, out var error));
        Assert.Contains("Missing", error, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaValidation_RejectsInvalidJson()
    {
        var json = "not valid json";
        Assert.False(ReferenceStatePreset.TryValidateSchema(json, out var error));
        Assert.Contains("Invalid JSON", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FromJson_WrongVersion_ReturnsNull()
    {
        var json = """{ "schemaVersion": 999, "entries": [] }""";
        Assert.Null(ReferenceStatePreset.FromJson(json));
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsNull()
    {
        Assert.Null(ReferenceStatePreset.FromJson("{garbage}"));
    }

    [Fact]
    public void MCP_ExportTool_WithNoModels_Fails()
    {
        var session = new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
        var tool = new ExportReferenceStatePresetMcpTool(session);

        var result = tool.Invoke(JsonSerializer.SerializeToElement(new
        {
            output_path = "/tmp/nonexistent/preset.json"
        }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("No reference models loaded", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MCP_ExportTool_ToFile_And_ImportTool_RoundTrip()
    {
        // Create a session with a loaded model
        var loggerFactory = NullLoggerFactory.Instance;
        var loader = new ReferenceModelLoader(loggerFactory.CreateLogger<ReferenceModelLoader>());
        var assetService = new ReferenceAssetService(loader);

        var session = new VoxelForgeMcpSession(new EditorConfigState(), loggerFactory);
        var loadTool = new LoadReferenceModelMcpTool(session, assetService);

        var loadResult = loadTool.Invoke(
            JsonSerializer.SerializeToElement(new { path = _objPath }),
            CancellationToken.None);
        Assert.True(loadResult.Success, loadResult.Message);

        // Apply transform and visibility changes
        var state = session.ReferenceModels;
        var model = state.Get(0);
        Assert.NotNull(model);
        model.PositionX = 5f;
        model.PositionY = 10f;
        model.PositionZ = -2f;
        model.RotationX = 90f;
        model.Scale = 1.5f;
        model.IsVisible = false;
        model.RenderMode = ReferenceRenderMode.Wireframe;

        // Export preset
        var exportPath = Path.Combine(_tempDir, "test_preset.json");
        var exportTool = new ExportReferenceStatePresetMcpTool(session);
        var exportResult = exportTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                output_path = exportPath,
                label = "Test Preset",
                notes = "Round-trip verification"
            }),
            CancellationToken.None);

        Assert.True(exportResult.Success, exportResult.Message);
        Assert.True(File.Exists(exportPath), "Preset file should exist");

        // Verify the JSON is valid by reading it back
        var savedJson = File.ReadAllText(exportPath);
        Assert.True(ReferenceStatePreset.TryValidateSchema(savedJson, out var schemaErr), schemaErr);
        var savedPreset = ReferenceStatePreset.FromJson(savedJson);
        Assert.NotNull(savedPreset);
        Assert.Equal("Test Preset", savedPreset.Label);
        Assert.Single(savedPreset.Entries);

        // Clear models
        session.ReferenceModels.Clear();
        Assert.Empty(session.ReferenceModels.Models);

        // Import preset
        var importTool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, loggerFactory);
        var importResult = importTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                preset_path = exportPath
            }),
            CancellationToken.None);

        Assert.True(importResult.Success, importResult.Message);
        Assert.Single(session.ReferenceModels.Models);

        // Verify restored state matches
        var restored = session.ReferenceModels.Get(0);
        Assert.NotNull(restored);
        Assert.Equal(5f, restored.PositionX);
        Assert.Equal(10f, restored.PositionY);
        Assert.Equal(-2f, restored.PositionZ);
        Assert.Equal(90f, restored.RotationX);
        Assert.Equal(0f, restored.RotationY); // not set, default
        Assert.Equal(0f, restored.RotationZ); // not set, default
        Assert.Equal(1.5f, restored.Scale);
        Assert.False(restored.IsVisible);
        Assert.Equal(ReferenceRenderMode.Wireframe, restored.RenderMode);
        Assert.Equal("OBJ", restored.Format);
    }

    [Fact]
    public void MCP_ExportTool_WithLabelAndNotes_SavesToFile()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var loader = new ReferenceModelLoader(loggerFactory.CreateLogger<ReferenceModelLoader>());
        var assetService = new ReferenceAssetService(loader);

        var session = new VoxelForgeMcpSession(new EditorConfigState(), loggerFactory);
        var loadTool = new LoadReferenceModelMcpTool(session, assetService);

        var loadResult = loadTool.Invoke(
            JsonSerializer.SerializeToElement(new { path = _objPath }),
            CancellationToken.None);
        Assert.True(loadResult.Success);

        var exportPath = Path.Combine(_tempDir, "labeled_preset.json");
        var exportTool = new ExportReferenceStatePresetMcpTool(session);
        var exportResult = exportTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                output_path = exportPath,
                label = "My Preset",
                notes = "Some notes"
            }),
            CancellationToken.None);

        Assert.True(exportResult.Success, exportResult.Message);

        // Verify JSON content
        var json = File.ReadAllText(exportPath);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("My Preset", doc.RootElement.GetProperty("label").GetString());
        Assert.Equal("Some notes", doc.RootElement.GetProperty("notes").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void MCP_ImportTool_MissingFile_Fails()
    {
        var session = new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
        var loader = new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance);
        var assetService = new ReferenceAssetService(loader);

        var tool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, NullLoggerFactory.Instance);
        var result = tool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                preset_path = "/tmp/nonexistent/preset.json"
            }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MCP_ImportTool_WithClearExisting_RemovesPreviousModels()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var loader = new ReferenceModelLoader(loggerFactory.CreateLogger<ReferenceModelLoader>());
        var assetService = new ReferenceAssetService(loader);

        var session = new VoxelForgeMcpSession(new EditorConfigState(), loggerFactory);
        var loadTool = new LoadReferenceModelMcpTool(session, assetService);

        // Load a model
        var loadResult = loadTool.Invoke(
            JsonSerializer.SerializeToElement(new { path = _objPath }),
            CancellationToken.None);
        Assert.True(loadResult.Success);

        // Export preset
        var exportPath = Path.Combine(_tempDir, "preset_to_clear.json");
        var exportTool = new ExportReferenceStatePresetMcpTool(session);
        var exportResult = exportTool.Invoke(
            JsonSerializer.SerializeToElement(new { output_path = exportPath }),
            CancellationToken.None);
        Assert.True(exportResult.Success);

        // Load a second model
        var loadResult2 = loadTool.Invoke(
            JsonSerializer.SerializeToElement(new { path = _objWithUvsPath }),
            CancellationToken.None);
        Assert.True(loadResult2.Success);
        Assert.Equal(2, session.ReferenceModels.Models.Count);

        // Import with clear_existing=true — should go back to 1
        var importTool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, loggerFactory);
        var importResult = importTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                preset_path = exportPath,
                clear_existing = true
            }),
            CancellationToken.None);

        Assert.True(importResult.Success, importResult.Message);
        Assert.Single(session.ReferenceModels.Models);
    }

    [Fact]
    public void MCP_ImportTool_AppendsByDefault()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var loader = new ReferenceModelLoader(loggerFactory.CreateLogger<ReferenceModelLoader>());
        var assetService = new ReferenceAssetService(loader);

        var session = new VoxelForgeMcpSession(new EditorConfigState(), loggerFactory);
        var loadTool = new LoadReferenceModelMcpTool(session, assetService);

        // Load a model
        var loadResult = loadTool.Invoke(
            JsonSerializer.SerializeToElement(new { path = _objPath }),
            CancellationToken.None);
        Assert.True(loadResult.Success);

        // Export preset
        var exportPath = Path.Combine(_tempDir, "preset_to_append.json");
        var exportTool = new ExportReferenceStatePresetMcpTool(session);
        var exportResult = exportTool.Invoke(
            JsonSerializer.SerializeToElement(new { output_path = exportPath }),
            CancellationToken.None);
        Assert.True(exportResult.Success);

        // Import without clearing — should keep existing + add from preset
        // (Preset has 1 entry, so total = 1 existing + 1 imported = 2)
        var importTool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, loggerFactory);
        var importResult = importTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                preset_path = exportPath
            }),
            CancellationToken.None);

        Assert.True(importResult.Success, importResult.Message);
        Assert.Equal(2, session.ReferenceModels.Models.Count);
    }

    [Fact]
    public void MCP_ImportTool_BadSchemaVersion_Fails()
    {
        var session = new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
        var loader = new ReferenceModelLoader(NullLogger<ReferenceModelLoader>.Instance);
        var assetService = new ReferenceAssetService(loader);

        // Write a bad-version preset
        var badPath = Path.Combine(_tempDir, "bad_version.json");
        File.WriteAllText(badPath, """{ "schemaVersion": 999, "entries": [] }""");

        var tool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, NullLoggerFactory.Instance);
        var result = tool.Invoke(
            JsonSerializer.SerializeToElement(new { preset_path = badPath }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Incompatible schema version", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MCP_ImportTool_DiffuseAndEmissiveOverride_PreservesBoth()
    {
        // This test validates that when a PresetMeshOverride has both
        // DiffuseTexturePath and EmissiveTexturePath on the same mesh,
        // the import pipeline chains rebakes correctly (the emissive rebake
        // uses the diffuse-rebaked mesh, not the original).

        var loggerFactory = NullLoggerFactory.Instance;
        var loader = new ReferenceModelLoader(loggerFactory.CreateLogger<ReferenceModelLoader>());
        var assetService = new ReferenceAssetService(loader);

        // Create valid PNG texture files
        var diffuseTexPath = Path.Combine(_tempDir, "override_diffuse.png");
        var emissiveTexPath = Path.Combine(_tempDir, "override_emissive.png");

        // 1x1 white PNG (valid base64)
        var tinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=");
        File.WriteAllBytes(diffuseTexPath, tinyPng);
        File.WriteAllBytes(emissiveTexPath, tinyPng);

        // Create a preset file with a single entry that has both diffuse and
        // emissive overrides on mesh index 0
        var presetPath = Path.Combine(_tempDir, "dual_override_preset.json");
        var presetJson = $$"""
        {
            "schemaVersion": 1,
            "label": "DualOverrideTest",
            "entries": [
                {
                    "sourcePath": "{{_objWithUvsPath}}",
                    "format": "OBJ",
                    "positionX": 0, "positionY": 0, "positionZ": 0,
                    "rotationX": 0, "rotationY": 0, "rotationZ": 0,
                    "scale": 1, "isVisible": true, "renderMode": "solid",
                    "meshOverrides": [
                        {
                            "meshIndex": 0,
                            "diffuseTexturePath": "{{diffuseTexPath}}",
                            "emissiveTexturePath": "{{emissiveTexPath}}",
                            "emissiveBrightness": 0.5
                        }
                    ]
                }
            ]
        }
        """;
        File.WriteAllText(presetPath, presetJson);

        var session = new VoxelForgeMcpSession(new EditorConfigState(), loggerFactory);

        var importTool = new ImportReferenceStatePresetMcpTool(
            session, loader, assetService, loggerFactory);
        var importResult = importTool.Invoke(
            JsonSerializer.SerializeToElement(new
            {
                preset_path = presetPath
            }),
            CancellationToken.None);

        Assert.True(importResult.Success, importResult.Message);
        Assert.Single(session.ReferenceModels.Models);

        var restored = session.ReferenceModels.Get(0);
        Assert.NotNull(restored);
        Assert.NotEmpty(restored.Meshes);

        var mesh = restored.Meshes[0];
        Assert.NotNull(mesh.DiffuseTexturePath);
        Assert.NotNull(mesh.EmissiveTexturePath);

        // Both override paths must be preserved — diffuse was not lost to the
        // emissive rebake
        Assert.Equal(diffuseTexPath, mesh.DiffuseTexturePath);
        Assert.Equal(emissiveTexPath, mesh.EmissiveTexturePath);
        Assert.Equal(0.5f, mesh.EmissiveBrightness);
    }

    // ── Helpers ──

    private static ReferenceModelData CreateSimpleModel(string fileName)
    {
        return new ReferenceModelData
        {
            FilePath = "/path/to/" + fileName,
            Format = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant(),
            Meshes =
            [
                new ReferenceMeshData
                {
                    Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 128, 128, 128, 255)],
                    Indices = [0, 0, 0],
                    MaterialName = "default",
                },
            ],
        };
    }
}
