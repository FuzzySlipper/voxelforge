using VoxelForge.Core.Reference;

namespace VoxelForge.App.Reference;

/// <summary>
/// Holds loaded reference models. Injected into commands and the renderer.
/// </summary>
public sealed class ReferenceModelRegistry
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

    /// <summary>
    /// Fired when models are added/removed so the renderer can rebuild GPU buffers.
    /// </summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
