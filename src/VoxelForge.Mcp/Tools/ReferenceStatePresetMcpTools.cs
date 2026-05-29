using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Services;
using VoxelForge.Content;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP tool: export_reference_state_preset — serialize all loaded reference
/// models and their per-mesh state (transform, texture overrides, texture
/// sampling, visibility, render mode, animation) to a versioned JSON preset
/// file. The preset can be reloaded later with import_reference_state_preset.
/// </summary>
public sealed class ExportReferenceStatePresetMcpTool : ModelLifecycleMcpToolBase
{
    public ExportReferenceStatePresetMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "export_reference_state_preset",
            "Export all loaded reference models and their state (transform, texture overrides, "
            + "texture sampling, visibility, render mode, animation state, provenance) to a "
            + "versioned JSON preset file. The output file can be reloaded with "
            + "import_reference_state_preset. Schema V1 uses absolute paths only — "
            + "project-relative resolution is not yet implemented.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "output_path": { "type": "string", "description": "Output file path for the JSON preset. Defaults to .vf-state-preset.json extension if none given." },
                    "label": { "type": "string", "description": "Optional human-readable label for the preset." },
                    "notes": { "type": "string", "description": "Optional provenance notes or workflow context." }
                },
                "required": ["output_path"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredString(arguments, "output_path", out var outputPath, out var errorMessage))
            return Fail(errorMessage);

        if (string.IsNullOrWhiteSpace(outputPath))
            return Fail("output_path cannot be empty.");

        // Ensure extension
        if (!outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            outputPath += ".vf-state-preset.json";

        outputPath = Path.GetFullPath(outputPath);

        string? label = null;
        string? notes = null;
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String)
                label = labelEl.GetString();
            if (arguments.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.String)
                notes = notesEl.GetString();
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = Session.ReferenceModels;
            if (state.Models.Count == 0)
                return Fail("No reference models loaded. Load at least one model before exporting a preset.");

            var preset = ReferenceStatePreset.FromModels(
                state.Models,
                label: label,
                notes: notes,
                createdBy: "VoxelForge MCP");

            try
            {
                var json = preset.ToJson();
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(outputPath, json);
            }
            catch (Exception ex)
            {
                return Fail($"Failed to write preset file: {ex.Message}");
            }

            return Ok($"Exported {preset.Entries.Count} reference model(s) to {outputPath} "
                      + $"(schema v{preset.SchemaVersion})");
        }
    }
}

public sealed class ExportReferenceStatePresetServerTool : VoxelForgeMcpServerTool
{
    public ExportReferenceStatePresetServerTool(ExportReferenceStatePresetMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// MCP tool: import_reference_state_preset — load a versioned JSON preset file
/// and restore all reference models with their transforms, texture overrides,
/// sampling controls, visibility, render mode, and animation state.
/// Missing source paths or incompatible schema versions produce clear errors.
/// Schema V1 uses absolute paths; no project-relative resolution.
/// </summary>
public sealed class ImportReferenceStatePresetMcpTool : ModelLifecycleMcpToolBase
{
    private readonly ReferenceModelLoader _loader;
    private readonly ReferenceAssetService _referenceAssetService;
    private readonly ILogger<ImportReferenceStatePresetMcpTool> _logger;

    public ImportReferenceStatePresetMcpTool(
        VoxelForgeMcpSession session,
        ReferenceModelLoader loader,
        ReferenceAssetService referenceAssetService,
        ILoggerFactory loggerFactory)
        : base(
            session,
            "import_reference_state_preset",
            "Load a versioned JSON preset file and restore all reference models with their "
            + "transforms, texture overrides, texture sampling controls, visibility, render mode, "
            + "and animation state. Returns warnings for missing source paths or mesh index mismatches. "
            + "Schema V1 uses absolute paths — project-relative resolution is not yet implemented. "
            + "Existing loaded models are preserved; new models are appended. "
            + "Use clear_existing=true to replace all current models.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "preset_path": { "type": "string", "description": "Path to the .vf-state-preset.json file." },
                    "clear_existing": { "type": "boolean", "description": "If true, remove all existing reference models before importing the preset. Default: false." }
                },
                "required": ["preset_path"]
            }
            """),
            isReadOnly: false)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(referenceAssetService);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loader = loader;
        _referenceAssetService = referenceAssetService;
        _logger = loggerFactory.CreateLogger<ImportReferenceStatePresetMcpTool>();
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredString(arguments, "preset_path", out var presetPath, out var errorMessage))
            return Fail(errorMessage);

        if (string.IsNullOrWhiteSpace(presetPath))
            return Fail("preset_path cannot be empty.");

        presetPath = Path.GetFullPath(presetPath);

        if (!File.Exists(presetPath))
            return Fail($"Preset file not found: {presetPath}");

        // Read and validate schema version
        string json;
        try
        {
            json = File.ReadAllText(presetPath);
        }
        catch (Exception ex)
        {
            return Fail($"Failed to read preset file: {ex.Message}");
        }

        if (!ReferenceStatePreset.TryValidateSchema(json, out var schemaError))
            return Fail($"Schema validation failed: {schemaError}");

        // Deserialize
        var preset = ReferenceStatePreset.FromJson(json);
        if (preset is null)
            return Fail("Failed to parse preset file.");

        if (preset.Entries.Count == 0)
            return Fail("Preset contains no entries.");

        bool clearExisting = false;
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("clear_existing", out var clearEl) &&
            clearEl.ValueKind == JsonValueKind.True)
        {
            clearExisting = true;
        }

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (clearExisting)
            {
                _referenceAssetService.ClearModels(Session.ReferenceModels, Session.Events);
            }

            var baseDir = Path.GetDirectoryName(presetPath);
            var warnings = ApplyPreset(preset, baseDir, cancellationToken);

            var result = $"Imported {preset.Entries.Count} reference model(s) from preset. "
                       + $"Label: {preset.Label ?? "(none)"}. "
                       + $"Schema v{preset.SchemaVersion}.";
            if (warnings.Count > 0)
                result += "\nWarnings:\n  " + string.Join("\n  ", warnings);

            return Ok(result);
        }
    }

    private List<string> ApplyPreset(
        ReferenceStatePreset preset,
        string? baseDirectory,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        foreach (var entry in preset.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = entry.SourcePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                warnings.Add("Skipping entry with empty source path.");
                continue;
            }

            // Resolve path: use baseDirectory if path is relative and baseDirectory is provided
            string resolvedSourcePath;
            if (!Path.IsPathRooted(sourcePath) && baseDirectory is not null)
                resolvedSourcePath = Path.GetFullPath(Path.Combine(baseDirectory, sourcePath));
            else
                resolvedSourcePath = Path.GetFullPath(sourcePath);

            if (!File.Exists(resolvedSourcePath))
            {
                warnings.Add($"Source path not found: {resolvedSourcePath}");
                continue;
            }

            ReferenceModelData model;
            try
            {
                model = _loader.Load(resolvedSourcePath);
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to load '{resolvedSourcePath}': {ex.Message}");
                continue;
            }

            // Apply model-level transform, visibility, render mode
            model.PositionX = entry.PositionX;
            model.PositionY = entry.PositionY;
            model.PositionZ = entry.PositionZ;
            model.RotationX = entry.RotationX;
            model.RotationY = entry.RotationY;
            model.RotationZ = entry.RotationZ;
            model.Scale = entry.Scale;
            model.IsVisible = entry.IsVisible;

            if (!string.IsNullOrWhiteSpace(entry.RenderMode))
            {
                if (Enum.TryParse<ReferenceRenderMode>(entry.RenderMode, ignoreCase: true, out var renderMode))
                    model.RenderMode = renderMode;
                else
                    warnings.Add($"Invalid render mode '{entry.RenderMode}' — keeping default.");
            }

            // Apply animation state
            if (entry.ActiveClipIndex.HasValue && model.HasAnimations)
            {
                model.ActiveClipIndex = entry.ActiveClipIndex;
                model.AnimationSpeed = entry.AnimationSpeed;
                model.IsAnimating = true;
            }

            // Apply per-mesh overrides
            if (entry.MeshOverrides is not null)
            {
                foreach (var ov in entry.MeshOverrides)
                {
                    if (ov.MeshIndex < 0 || ov.MeshIndex >= model.Meshes.Count)
                    {
                        warnings.Add($"Mesh index {ov.MeshIndex} out of range (model has {model.Meshes.Count} meshes), skipped.");
                        continue;
                    }

                    var mesh = model.Meshes[ov.MeshIndex];

                    // Apply sampling controls first (before re-baking)
                    if (ov.UvOrigin is not null)
                        mesh.UvOrigin = ov.UvOrigin;
                    if (ov.FlipY is not null)
                        mesh.FlipY = ov.FlipY;
                    if (ov.WrapS is not null)
                        mesh.WrapS = ov.WrapS;
                    if (ov.WrapT is not null)
                        mesh.WrapT = ov.WrapT;
                    if (ov.SamplingControlsSource is not null)
                        mesh.SamplingControlsSource = ov.SamplingControlsSource;

                    // Manual override paths
                    if (ov.ManualDiffuseOverridePath is not null)
                        mesh.ManualDiffuseOverridePath = ov.ManualDiffuseOverridePath;
                    if (ov.ManualNormalOverridePath is not null)
                        mesh.ManualNormalOverridePath = ov.ManualNormalOverridePath;
                    if (ov.ManualEmissiveOverridePath is not null)
                        mesh.ManualEmissiveOverridePath = ov.ManualEmissiveOverridePath;

                    // Re-bake diffuse texture if a source path is present
                    if (ov.DiffuseTexturePath is not null)
                    {
                        string texPath;
                        if (!Path.IsPathRooted(ov.DiffuseTexturePath) && baseDirectory is not null)
                            texPath = Path.GetFullPath(Path.Combine(baseDirectory, ov.DiffuseTexturePath));
                        else
                            texPath = Path.GetFullPath(ov.DiffuseTexturePath);

                        if (File.Exists(texPath))
                        {
                            var newMesh = _loader.Retexture(mesh, texPath);
                            if (newMesh is not null)
                            {
                                // Preserve manual overrides and sampling on the new mesh
                                newMesh.ManualDiffuseOverridePath = mesh.ManualDiffuseOverridePath;
                                newMesh.ManualNormalOverridePath = mesh.ManualNormalOverridePath;
                                newMesh.ManualEmissiveOverridePath = mesh.ManualEmissiveOverridePath;
                                newMesh.UvOrigin = mesh.UvOrigin;
                                newMesh.FlipY = mesh.FlipY;
                                newMesh.WrapS = mesh.WrapS;
                                newMesh.WrapT = mesh.WrapT;
                                newMesh.SamplingControlsSource = mesh.SamplingControlsSource;
                                model.Meshes[ov.MeshIndex] = newMesh;
                                mesh = newMesh; // chain: subsequent rebakes use this rebaked mesh
                            }
                            else
                            {
                                warnings.Add($"Mesh {ov.MeshIndex}: failed to re-bake diffuse texture.");
                            }
                        }
                        else
                        {
                            warnings.Add($"Mesh {ov.MeshIndex}: diffuse texture not found: {texPath}");
                        }
                    }

                    // Re-bake emissive texture
                    if (ov.EmissiveTexturePath is not null)
                    {
                        string texPath;
                        if (!Path.IsPathRooted(ov.EmissiveTexturePath) && baseDirectory is not null)
                            texPath = Path.GetFullPath(Path.Combine(baseDirectory, ov.EmissiveTexturePath));
                        else
                            texPath = Path.GetFullPath(ov.EmissiveTexturePath);

                        if (File.Exists(texPath))
                        {
                            var newMesh = _loader.RetextureEmissive(
                                mesh, texPath, ov.EmissiveBrightness ?? 1f);
                            if (newMesh is not null)
                            {
                                newMesh.ManualDiffuseOverridePath = mesh.ManualDiffuseOverridePath;
                                newMesh.ManualNormalOverridePath = mesh.ManualNormalOverridePath;
                                newMesh.ManualEmissiveOverridePath = mesh.ManualEmissiveOverridePath;
                                newMesh.UvOrigin = mesh.UvOrigin;
                                newMesh.FlipY = mesh.FlipY;
                                newMesh.WrapS = mesh.WrapS;
                                newMesh.WrapT = mesh.WrapT;
                                newMesh.SamplingControlsSource = mesh.SamplingControlsSource;
                                model.Meshes[ov.MeshIndex] = newMesh;
                            }
                            else
                            {
                                warnings.Add($"Mesh {ov.MeshIndex}: failed to re-bake emissive texture.");
                            }
                        }
                        else
                        {
                            warnings.Add($"Mesh {ov.MeshIndex}: emissive texture not found: {texPath}");
                        }
                    }
                }
            }

            Session.ReferenceModels.Add(model);

            Session.Events.Publish(new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.Loaded,
                $"Imported preset entry: {Path.GetFileName(entry.SourcePath)}",
                Session.ReferenceModels.Models.Count - 1));
        }

        return warnings;
    }
}

public sealed class ImportReferenceStatePresetServerTool : VoxelForgeMcpServerTool
{
    public ImportReferenceStatePresetServerTool(ImportReferenceStatePresetMcpTool tool)
        : base(tool)
    {
    }
}
