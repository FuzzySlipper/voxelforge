using System.Text.Json;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// MCP tool: inspect_reference_materials — list meshes, material names,
/// texture slots, source labels, and override status for a loaded reference model.
/// Returns compact agent-readable JSON per mesh.
/// </summary>
public sealed class InspectReferenceMaterialsMcpTool : ModelLifecycleMcpToolBase
{
    public InspectReferenceMaterialsMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "inspect_reference_materials",
            "Inspect loaded reference model materials/meshes and texture slots in compact agent-readable JSON. " +
            "Includes model index, mesh index, material name, current diffuse base-color path, " +
            "normal/emissive/alpha status, texture paths if known, and source labels " +
            "(assimp/imported, unity_sidecar, manual_override, or none). Session-only overrides are clearly labelled.",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." }
                },
                "required": ["index"]
            }
            """),
            isReadOnly: true)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            var meshes = new List<Dictionary<string, object?>>();
            for (int mi = 0; mi < model.Meshes.Count; mi++)
            {
                var mesh = model.Meshes[mi];
                var entry = new Dictionary<string, object?>
                {
                    ["mesh_index"] = mi,
                    ["material_name"] = mesh.MaterialName,
                    ["diffuse_texture_path"] = mesh.EffectiveDiffuseTexturePath,
                    ["diffuse_source_label"] = mesh.DiffuseSourceLabel,
                    ["has_manual_override"] = mesh.ManualDiffuseOverridePath is not null,
                    ["has_assimp_texture"] = mesh.DiffuseTexturePath is not null,
                };

                // Normal texture info
                if (mesh.ManualNormalOverridePath is not null)
                {
                    entry["normal_texture_path"] = mesh.ManualNormalOverridePath;
                    entry["normal_source_label"] = "manual_override";
                }
                else
                {
                    entry["normal_source_label"] = "none";
                }

                // Emissive texture info
                entry["emissive_texture_path"] = mesh.EffectiveEmissiveTexturePath;
                entry["emissive_source_label"] = mesh.ManualEmissiveOverridePath is not null
                    ? "manual_override"
                    : (mesh.EmissiveTexturePath is not null ? "unity_sidecar" : "none");

                // Vertex count and alpha status
                bool hasAlpha = false;
                if (mesh.Vertices.Length > 0)
                {
                    foreach (var v in mesh.Vertices)
                    {
                        if (v.A < 255)
                        {
                            hasAlpha = true;
                            break;
                        }
                    }
                }
                entry["has_vertex_alpha"] = hasAlpha;
                entry["vertex_count"] = mesh.Vertices.Length;

                meshes.Add(entry);
            }

            var result = new Dictionary<string, object?>
            {
                ["model_index"] = index,
                ["file_name"] = Path.GetFileName(model.FilePath),
                ["format"] = model.Format,
                ["mesh_count"] = model.Meshes.Count,
                ["meshes"] = meshes,
            };

            return Ok(SerializeJson(result));
        }
    }
}

public sealed class InspectReferenceMaterialsServerTool : VoxelForgeMcpServerTool
{
    public InspectReferenceMaterialsServerTool(InspectReferenceMaterialsMcpTool tool)
        : base(tool)
    {
    }
}

/// <summary>
/// MCP tool: set_reference_model_texture — manually assign a texture file
/// to a loaded reference model material or mesh, at minimum diffuse/base-color;
/// normal and emissive slots supported. Validates file existence and supported
/// image formats (.png/.jpg/.jpeg/.bmp/.tga). Invalid paths/formats fail without mutation.
/// Override metadata is visible through diagnostics/listing immediately,
/// including viewer revision/SSE updates through ReferenceModelChangedEvent.
/// Overrides are session-only — clearly labelled as manual_override in diagnostics.
/// </summary>
public sealed class SetReferenceModelTextureMcpTool : ModelLifecycleMcpToolBase
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".webp",
    };

    public SetReferenceModelTextureMcpTool(VoxelForgeMcpSession session)
        : base(
            session,
            "set_reference_model_texture",
            "Manually assign a texture file to a loaded reference model material or mesh. " +
            "Supports diffuse/base-color (slot='diffuse'), normal (slot='normal'), and emissive (slot='emissive') slots. " +
            "Target by mesh_index (integer) or material_name (string). " +
            "Validates file existence and supported formats (.png/.jpg/.jpeg/.bmp/.tga/.webp). " +
            "Invalid paths/formats fail without mutation. " +
            "Session-only overrides — labelled as manual_override in diagnostics. " +
            "Immediately affects viewer rendering and diagnostics (triggers ReferenceModelChangedEvent via TextureChanged kind).",
            McpJsonSchemas.Parse("""
            {
                "type": "object",
                "properties": {
                    "index": { "type": "integer", "description": "Reference model index." },
                    "mesh_index": { "type": "integer", "description": "Target mesh index. Mutually exclusive with material_name." },
                    "material_name": { "type": "string", "description": "Target material name (applies to all meshes with matching name). Mutually exclusive with mesh_index." },
                    "slot": { "type": "string", "enum": ["diffuse", "normal", "emissive"], "description": "Texture slot to assign." },
                    "path": { "type": "string", "description": "Absolute or relative path to the texture file. Must exist on disk and have a supported extension." }
                },
                "required": ["index", "slot", "path"]
            }
            """),
            isReadOnly: false)
    {
    }

    public override McpToolInvocationResult Invoke(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredInt(arguments, "index", out int index, out var errorMessage))
            return Fail(errorMessage);

        if (!TryReadRequiredString(arguments, "slot", out var slot, out errorMessage))
            return Fail(errorMessage);

        if (!TryReadRequiredString(arguments, "path", out var path, out errorMessage))
            return Fail(errorMessage);

        // Validate slot value
        slot = slot.ToLowerInvariant();
        if (slot is not "diffuse" and not "normal" and not "emissive")
            return Fail("Slot must be one of: 'diffuse', 'normal', 'emissive'.");

        // Resolve path
        path = Path.GetFullPath(path);

        // Validate file exists
        if (!File.Exists(path))
            return Fail($"Texture file not found: {path}");

        // Validate extension
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext) || !SupportedExtensions.Contains(ext))
            return Fail($"Unsupported texture format '{ext}'. Supported formats: .png, .jpg, .jpeg, .bmp, .tga, .webp");

        // Determine targeting
        bool hasMeshIndex = arguments.TryGetProperty("mesh_index", out var meshIndexEl) && meshIndexEl.ValueKind == JsonValueKind.Number;
        bool hasMaterialName = arguments.TryGetProperty("material_name", out var matNameEl) && matNameEl.ValueKind == JsonValueKind.String;
        string? materialName = hasMaterialName ? matNameEl.GetString() : null;

        if (hasMeshIndex && hasMaterialName)
            return Fail("Specify either 'mesh_index' or 'material_name', not both.");

        lock (Session.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = Session.ReferenceModels.Get(index);
            if (model is null)
                return Fail($"No reference model at index {index}.");

            int meshesAffected = 0;

            if (hasMeshIndex)
            {
                if (!meshIndexEl.TryGetInt32(out int meshIndex))
                    return Fail("Property 'mesh_index' must be an integer.");
                if (meshIndex < 0 || meshIndex >= model.Meshes.Count)
                    return Fail($"Mesh index {meshIndex} out of range. Model has {model.Meshes.Count} meshes (0-{model.Meshes.Count - 1}).");

                ApplyOverride(model.Meshes[meshIndex], slot, path);
                meshesAffected = 1;
            }
            else if (hasMaterialName && !string.IsNullOrWhiteSpace(materialName))
            {
                foreach (var mesh in model.Meshes)
                {
                    if (string.Equals(mesh.MaterialName, materialName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyOverride(mesh, slot, path);
                        meshesAffected++;
                    }
                }
                if (meshesAffected == 0)
                    return Fail($"No mesh found with material name matching '{materialName}'.");
            }
            else
            {
                // Default: apply to first mesh
                ApplyOverride(model.Meshes[0], slot, path);
                meshesAffected = 1;
            }

            // Publish event to trigger viewer revision increment and SSE update
            Session.Events.Publish(new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.TextureChanged,
                $"Manual texture override: {slot} <- {Path.GetFileName(path)} ({meshesAffected} mesh(es))",
                index));

            return Ok($"Assigned {slot} texture '{Path.GetFileName(path)}' to {meshesAffected} mesh(es) in model [{index}]. " +
                      "This is a session-only override and will not persist across restarts. " +
                      "Use inspect_reference_materials to verify.");
        }
    }

    private static void ApplyOverride(ReferenceMeshData mesh, string slot, string path)
    {
        switch (slot)
        {
            case "diffuse":
                mesh.ManualDiffuseOverridePath = path;
                break;
            case "normal":
                mesh.ManualNormalOverridePath = path;
                break;
            case "emissive":
                mesh.ManualEmissiveOverridePath = path;
                break;
        }
    }
}

public sealed class SetReferenceModelTextureServerTool : VoxelForgeMcpServerTool
{
    public SetReferenceModelTextureServerTool(SetReferenceModelTextureMcpTool tool)
        : base(tool)
    {
    }
}
