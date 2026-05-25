using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.App.Workspaces;

namespace VoxelForge.App.Services;

/// <summary>
/// Stateless service for reference-model operations.
/// Operates over explicit <see cref="VoxelForgeWorkspaceState"/> arguments
/// and does not hide mutable state in service instance fields.
/// <para>
/// Consolidates reference-model operations currently spread across MCP reference
/// tools and viewer endpoints. Transport hosts (MCP, Bridge) remain responsible
/// for how a texture handle becomes a URL; this service decides whether a texture
/// or source asset is authorized for the current loaded session.
/// </para>
/// </summary>
public sealed class ReferenceModelApplicationService
{
    /// <summary>
    /// Remove a reference model at the given index from the workspace.
    /// </summary>
    public ApplicationServiceResult<bool> RemoveReferenceModel(
        VoxelForgeWorkspaceState workspace,
        int index)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var model = workspace.ReferenceModels.Get(index);
        if (model is null)
        {
            return new ApplicationServiceResult<bool>
            {
                Success = false,
                Message = $"No reference model at index {index}.",
                Data = false,
            };
        }

        workspace.ReferenceModels.RemoveAt(index);
        workspace.IncrementRevision();
        workspace.StatusMessage = $"Removed reference model: {Path.GetFileName(model.FilePath)}";

        return new ApplicationServiceResult<bool>
        {
            Success = true,
            Message = $"Removed reference model: {Path.GetFileName(model.FilePath)}",
            Data = true,
            Events = [new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.Removed,
                $"Removed reference model: {Path.GetFileName(model.FilePath)}",
                null)],
        };
    }

    /// <summary>
    /// Clear all reference models from the workspace.
    /// </summary>
    public ApplicationServiceResult<bool> ClearReferenceModels(
        VoxelForgeWorkspaceState workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        int count = workspace.ReferenceModels.Models.Count;
        if (count == 0)
        {
            return new ApplicationServiceResult<bool>
            {
                Success = true,
                Message = "No reference models to clear.",
                Data = true,
            };
        }

        workspace.ReferenceModels.Clear();
        workspace.IncrementRevision();
        workspace.StatusMessage = $"Cleared {count} reference model(s).";

        return new ApplicationServiceResult<bool>
        {
            Success = true,
            Message = $"Cleared {count} reference model(s).",
            Data = true,
            Events = [new ReferenceModelChangedEvent(
                ReferenceModelChangeKind.Cleared,
                $"Cleared {count} reference model(s).",
                null)],
        };
    }

    /// <summary>
    /// Check whether the given texture path is authorized for the current workspace.
    /// Authorization checks that the texture is referenced by a currently loaded
    /// reference model.
    /// </summary>
    public bool IsTextureAuthorized(
        VoxelForgeWorkspaceState workspace,
        string texturePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(texturePath);

        foreach (var model in workspace.ReferenceModels.Models)
        {
            foreach (var mesh in model.Meshes)
            {
                if (string.Equals(mesh.EffectiveDiffuseTexturePath, texturePath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(mesh.EffectiveNormalTexturePath, texturePath, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(mesh.EffectiveEmissiveTexturePath, texturePath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
