using VoxelForge.Core.Reference;

namespace VoxelForge.App.Reference;

/// <summary>
/// Holds loaded reference models. Injected into commands and the renderer.
/// </summary>
public sealed class ReferenceModelState
{
    private readonly List<ReferenceModelData> _models = [];

    public IReadOnlyList<ReferenceModelData> Models => _models;

    public void Add(ReferenceModelData model) => _models.Add(model);

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _models.Count)
            _models.RemoveAt(index);
    }

    public ReferenceModelData? Get(int index)
    {
        return index >= 0 && index < _models.Count ? _models[index] : null;
    }

    public void Clear() => _models.Clear();
}
