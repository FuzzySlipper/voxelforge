namespace VoxelForge.Core.Meshing;

/// <summary>
/// Strategy interface for converting a VoxelModel into renderable mesh data.
/// Implementations are stateless — given a model, produce a mesh.
/// </summary>
public interface IVoxelMesher
{
    VoxelMesh Build(VoxelModel model);
}
