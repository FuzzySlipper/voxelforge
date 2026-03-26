using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace VoxelForge.Core.Tests;

public sealed class AnimationClipTests
{
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);
    private static readonly ILogger<VoxelModel> ModelLogger = NullLogger<VoxelModel>.Instance;

    private static AnimationClip CreateClip(VoxelModel baseModel, string name = "walk")
        => new(baseModel, NullLogger<AnimationClip>.Instance) { Name = name };

    [Fact]
    public void ResolveFrame_AppliesOverridesToBase()
    {
        var baseModel = CreateModel();
        // Set up 100 base voxels
        for (int i = 0; i < 100; i++)
            baseModel.SetVoxel(new Point3(i, 0, 0), 1);

        var clip = CreateClip(baseModel);

        // Add 4 frames with 10 overrides each
        for (int f = 0; f < 4; f++)
        {
            clip.AddFrame();
            for (int i = 0; i < 10; i++)
                clip.SetFrameOverride(f, new Point3(i, 0, 0), (byte)(f + 2));
        }

        // Verify each frame resolves correctly
        for (int f = 0; f < 4; f++)
        {
            var resolved = clip.ResolveFrame(f, ModelLogger);

            // Overridden voxels have the frame-specific value
            for (int i = 0; i < 10; i++)
                Assert.Equal((byte)(f + 2), resolved.GetVoxel(new Point3(i, 0, 0)));

            // Non-overridden voxels retain the base value
            for (int i = 10; i < 100; i++)
                Assert.Equal((byte)1, resolved.GetVoxel(new Point3(i, 0, 0)));
        }
    }

    [Fact]
    public void ResolveFrame_BaseEditPropagates_WhenNotOverridden()
    {
        var baseModel = CreateModel();
        baseModel.SetVoxel(new Point3(0, 0, 0), 1);
        baseModel.SetVoxel(new Point3(1, 0, 0), 1);

        var clip = CreateClip(baseModel);
        clip.AddFrame();
        // Override only position (0,0,0)
        clip.SetFrameOverride(0, new Point3(0, 0, 0), 5);

        // Edit the base at a non-overridden position
        baseModel.SetVoxel(new Point3(1, 0, 0), 9);

        var resolved = clip.ResolveFrame(0, ModelLogger);

        // Overridden voxel keeps override value
        Assert.Equal((byte)5, resolved.GetVoxel(new Point3(0, 0, 0)));
        // Non-overridden voxel reflects base change
        Assert.Equal((byte)9, resolved.GetVoxel(new Point3(1, 0, 0)));
    }

    [Fact]
    public void ResolveFrame_NullOverride_RemovesBaseVoxel()
    {
        var baseModel = CreateModel();
        baseModel.SetVoxel(new Point3(5, 5, 5), 1);

        var clip = CreateClip(baseModel);
        clip.AddFrame();
        clip.SetFrameOverride(0, new Point3(5, 5, 5), null);

        var resolved = clip.ResolveFrame(0, ModelLogger);
        Assert.Null(resolved.GetVoxel(new Point3(5, 5, 5)));
    }

    [Fact]
    public void SetFrameOverride_SameValueAsBase_StillStored()
    {
        var baseModel = CreateModel();
        baseModel.SetVoxel(new Point3(0, 0, 0), 3);

        var clip = CreateClip(baseModel);
        clip.AddFrame();
        // Override with the same value as base — explicit override
        clip.SetFrameOverride(0, new Point3(0, 0, 0), 3);

        Assert.Equal(1, clip.GetOverrideCount(0));
    }

    [Fact]
    public void ResolveFrame_ReturnsValidVoxelModel()
    {
        var baseModel = CreateModel();
        baseModel.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        baseModel.SetVoxel(new Point3(0, 0, 0), 1);
        baseModel.SetVoxel(new Point3(1, 0, 0), 1);

        var clip = CreateClip(baseModel);
        clip.AddFrame();
        clip.SetFrameOverride(0, new Point3(2, 0, 0), 1); // Add a voxel

        var resolved = clip.ResolveFrame(0, ModelLogger);

        Assert.NotNull(resolved);
        Assert.Equal(3, resolved.GetVoxelCount());
        Assert.Equal("Stone", resolved.Palette.Get(1)!.Name);
    }

    [Fact]
    public void AddFrame_IncreasesCount()
    {
        var clip = CreateClip(CreateModel());

        Assert.Empty(clip.Frames);
        clip.AddFrame();
        Assert.Single(clip.Frames);
        clip.AddFrame();
        Assert.Equal(2, clip.Frames.Count);
    }

    [Fact]
    public void RemoveFrame_DecreasesCount()
    {
        var clip = CreateClip(CreateModel());
        clip.AddFrame();
        clip.AddFrame();
        clip.AddFrame();

        clip.RemoveFrame(1);
        Assert.Equal(2, clip.Frames.Count);
    }

    [Fact]
    public void ClearFrameOverride_RemovesOverride()
    {
        var clip = CreateClip(CreateModel());
        clip.AddFrame();
        clip.SetFrameOverride(0, new Point3(0, 0, 0), 1);
        Assert.Equal(1, clip.GetOverrideCount(0));

        clip.ClearFrameOverride(0, new Point3(0, 0, 0));
        Assert.Equal(0, clip.GetOverrideCount(0));
    }

    [Fact]
    public void FrameRate_DefaultsTo12()
    {
        var clip = CreateClip(CreateModel());
        Assert.Equal(12, clip.FrameRate);
    }

    [Fact]
    public void FrameDuration_DefaultsToNull()
    {
        var clip = CreateClip(CreateModel());
        clip.AddFrame();
        Assert.Null(clip.Frames[0].Duration);
    }
}
