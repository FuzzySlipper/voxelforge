using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core;

namespace VoxelForge.App.Commands;

/// <summary>
/// Replaces the open document contents as one undoable operation.
/// </summary>
public sealed class ReplaceDocumentCommand : IEditorCommand
{
    private readonly EditorDocumentState _document;
    private readonly DocumentSnapshot _oldSnapshot;
    private readonly DocumentSnapshot _newSnapshot;

    public string Description { get; }

    public ReplaceDocumentCommand(
        EditorDocumentState document,
        VoxelModel newModel,
        LabelIndex newLabels,
        IReadOnlyList<AnimationClip> newClips,
        string description)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newModel);
        ArgumentNullException.ThrowIfNull(newLabels);
        ArgumentNullException.ThrowIfNull(newClips);
        ArgumentNullException.ThrowIfNull(description);

        _document = document;
        _oldSnapshot = DocumentSnapshot.Capture(document.Model, document.Labels, document.Clips);
        _newSnapshot = DocumentSnapshot.Capture(newModel, newLabels, newClips);
        Description = description;
    }

    public void Execute()
    {
        _newSnapshot.ApplyTo(_document);
    }

    public void Undo()
    {
        _oldSnapshot.ApplyTo(_document);
    }

    private sealed class DocumentSnapshot
    {
        private readonly Dictionary<Point3, byte> _voxels;
        private readonly Dictionary<byte, MaterialDef> _paletteEntries;
        private readonly List<RegionDef> _regions;
        private readonly List<ClipSnapshot> _clips;
        private readonly int _gridHint;

        private DocumentSnapshot(
            Dictionary<Point3, byte> voxels,
            Dictionary<byte, MaterialDef> paletteEntries,
            List<RegionDef> regions,
            List<ClipSnapshot> clips,
            int gridHint)
        {
            _voxels = voxels;
            _paletteEntries = paletteEntries;
            _regions = regions;
            _clips = clips;
            _gridHint = gridHint;
        }

        public static DocumentSnapshot Capture(
            VoxelModel model,
            LabelIndex labels,
            IReadOnlyList<AnimationClip> clips)
        {
            var voxels = new Dictionary<Point3, byte>();
            foreach (var entry in model.Voxels)
                voxels[entry.Key] = entry.Value;

            var paletteEntries = new Dictionary<byte, MaterialDef>();
            foreach (var entry in model.Palette.Entries)
                paletteEntries[entry.Key] = SetPaletteMaterialCommand.CloneMaterial(entry.Value);

            var regions = new List<RegionDef>();
            foreach (var entry in labels.Regions)
            {
                var clone = CreateRegionCommand.CloneRegion(entry.Value);
                clone.Voxels.Clear();
                foreach (var position in entry.Value.Voxels)
                {
                    if (voxels.ContainsKey(position))
                        clone.Voxels.Add(position);
                }
                regions.Add(clone);
            }

            var clipSnapshots = new List<ClipSnapshot>(clips.Count);
            for (int i = 0; i < clips.Count; i++)
                clipSnapshots.Add(ClipSnapshot.Capture(clips[i]));

            return new DocumentSnapshot(voxels, paletteEntries, regions, clipSnapshots, model.GridHint);
        }

        public void ApplyTo(EditorDocumentState document)
        {
            var oldVoxelPositions = new List<Point3>();
            foreach (var position in document.Model.Voxels.Keys)
                oldVoxelPositions.Add(position);
            for (int i = 0; i < oldVoxelPositions.Count; i++)
                document.Model.RemoveVoxel(oldVoxelPositions[i]);

            var oldPaletteEntries = new List<byte>();
            foreach (var entry in document.Model.Palette.Entries)
                oldPaletteEntries.Add(entry.Key);
            for (int i = 0; i < oldPaletteEntries.Count; i++)
                document.Model.Palette.Remove(oldPaletteEntries[i]);

            document.Model.GridHint = _gridHint;

            foreach (var entry in _paletteEntries)
                document.Model.Palette.Set(entry.Key, SetPaletteMaterialCommand.CloneMaterial(entry.Value));

            foreach (var entry in _voxels)
                document.Model.SetVoxel(entry.Key, entry.Value);

            var regionClones = new List<RegionDef>(_regions.Count);
            for (int i = 0; i < _regions.Count; i++)
                regionClones.Add(CreateRegionCommand.CloneRegion(_regions[i]));
            document.Labels.Rebuild(regionClones);

            document.Clips.Clear();
            for (int i = 0; i < _clips.Count; i++)
                document.Clips.Add(_clips[i].CreateClip(document.Model));
        }
    }

    private sealed class ClipSnapshot
    {
        private readonly string _name;
        private readonly int _frameRate;
        private readonly List<FrameSnapshot> _frames;

        private ClipSnapshot(string name, int frameRate, List<FrameSnapshot> frames)
        {
            _name = name;
            _frameRate = frameRate;
            _frames = frames;
        }

        public static ClipSnapshot Capture(AnimationClip clip)
        {
            var frames = new List<FrameSnapshot>(clip.Frames.Count);
            for (int i = 0; i < clip.Frames.Count; i++)
                frames.Add(FrameSnapshot.Capture(clip.Frames[i]));

            return new ClipSnapshot(clip.Name, clip.FrameRate, frames);
        }

        public AnimationClip CreateClip(VoxelModel baseModel)
        {
            var clip = new AnimationClip(baseModel, NullLogger<AnimationClip>.Instance)
            {
                Name = _name,
                FrameRate = _frameRate,
            };

            for (int i = 0; i < _frames.Count; i++)
            {
                clip.AddFrame();
                _frames[i].ApplyTo(clip.Frames[i]);
            }

            return clip;
        }
    }

    private sealed class FrameSnapshot
    {
        private readonly Dictionary<Point3, byte?> _voxelOverrides;
        private readonly Dictionary<Point3, RegionId?> _labelOverrides;
        private readonly float? _duration;

        private FrameSnapshot(
            Dictionary<Point3, byte?> voxelOverrides,
            Dictionary<Point3, RegionId?> labelOverrides,
            float? duration)
        {
            _voxelOverrides = voxelOverrides;
            _labelOverrides = labelOverrides;
            _duration = duration;
        }

        public static FrameSnapshot Capture(AnimationFrame frame)
        {
            var voxelOverrides = new Dictionary<Point3, byte?>();
            foreach (var entry in frame.VoxelOverrides)
                voxelOverrides[entry.Key] = entry.Value;

            var labelOverrides = new Dictionary<Point3, RegionId?>();
            foreach (var entry in frame.LabelOverrides)
                labelOverrides[entry.Key] = entry.Value;

            return new FrameSnapshot(voxelOverrides, labelOverrides, frame.Duration);
        }

        public void ApplyTo(AnimationFrame frame)
        {
            frame.Duration = _duration;
            foreach (var entry in _voxelOverrides)
                frame.VoxelOverrides[entry.Key] = entry.Value;
            foreach (var entry in _labelOverrides)
                frame.LabelOverrides[entry.Key] = entry.Value;
        }
    }
}
