namespace VoxelForge.App.Reference;

/// <summary>
/// A loaded reference image. Stores raw bytes — no engine types.
/// </summary>
public sealed class ReferenceImageEntry
{
    public required string FilePath { get; init; }
    public required byte[] RawBytes { get; init; }
    public string Label => Path.GetFileName(FilePath);
}

/// <summary>
/// Holds loaded reference images for display in the editor.
/// </summary>
public sealed class ReferenceImageStore
{
    private readonly List<ReferenceImageEntry> _images = [];

    public IReadOnlyList<ReferenceImageEntry> Images => _images;

    public void Add(ReferenceImageEntry entry) => _images.Add(entry);

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < _images.Count)
            _images.RemoveAt(index);
    }

    public ReferenceImageEntry? Get(int index)
    {
        return index >= 0 && index < _images.Count ? _images[index] : null;
    }

    /// <summary>Fired when images are added/removed so the UI can rebuild.</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();
}
