using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core.Serialization;

namespace VoxelForge.Core.Tests;

public sealed class SerializationTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);
    private static LabelIndex CreateIndex() => new(NullLogger<LabelIndex>.Instance);
    private static ProjectSerializer CreateSerializer() => new(LogFactory);

    [Fact]
    public void RoundTrip_PreservesAllData()
    {
        var model = CreateModel();
        model.Palette.Set(1, new MaterialDef { Name = "Stone", Color = new RgbaColor(128, 128, 128) });
        model.Palette.Set(2, new MaterialDef { Name = "Wood", Color = new RgbaColor(139, 90, 43) });

        // 500 voxels
        for (int i = 0; i < 500; i++)
            model.SetVoxel(new Point3(i % 32, (i / 32) % 32, i / 1024), (byte)(i % 2 == 0 ? 1 : 2));

        // 3 regions
        var labels = CreateIndex();
        labels.AddOrUpdateRegion(new RegionDef { Id = new RegionId("body"), Name = "body" });
        labels.AddOrUpdateRegion(new RegionDef
        {
            Id = new RegionId("right_arm"), Name = "right_arm",
            ParentId = new RegionId("body"),
        });
        labels.AddOrUpdateRegion(new RegionDef
        {
            Id = new RegionId("left_arm"), Name = "left_arm",
            ParentId = new RegionId("body"),
        });

        labels.AssignRegion(new RegionId("body"),
            Enumerable.Range(0, 10).Select(i => new Point3(i, 0, 0)));
        labels.AssignRegion(new RegionId("right_arm"),
            Enumerable.Range(10, 5).Select(i => new Point3(i, 0, 0)));

        // 4-frame animation
        var clip = new AnimationClip(model, NullLogger<AnimationClip>.Instance) { Name = "walk", FrameRate = 10 };
        for (int f = 0; f < 4; f++)
        {
            clip.AddFrame();
            for (int i = 0; i < 5; i++)
                clip.SetFrameOverride(f, new Point3(i, f, 0), (byte)(f + 1));
        }

        var meta = new ProjectMetadata { Name = "TestProject", Author = "George" };
        var serializer = CreateSerializer();

        // Serialize
        string json = serializer.Serialize(model, labels, [clip], meta);

        // Deserialize
        var (loadedModel, loadedLabels, loadedClips, loadedMeta) = serializer.Deserialize(json);

        // Verify model
        Assert.Equal(500, loadedModel.GetVoxelCount());
        Assert.Equal(model.GridHint, loadedModel.GridHint);
        foreach (var (pos, val) in model.Voxels)
            Assert.Equal(val, loadedModel.GetVoxel(pos));

        // Verify palette
        Assert.Equal("Stone", loadedModel.Palette.Get(1)!.Name);
        Assert.Equal("Wood", loadedModel.Palette.Get(2)!.Name);
        Assert.Equal(new RgbaColor(128, 128, 128), loadedModel.Palette.Get(1)!.Color);

        // Verify regions
        Assert.Equal(10, loadedLabels.GetVoxelsInRegion(new RegionId("body")).Count);
        Assert.Equal(5, loadedLabels.GetVoxelsInRegion(new RegionId("right_arm")).Count);
        Assert.Equal(new RegionId("body"),
            loadedLabels.Regions[new RegionId("right_arm")].ParentId);

        // Verify animation
        Assert.Single(loadedClips);
        Assert.Equal("walk", loadedClips[0].Name);
        Assert.Equal(10, loadedClips[0].FrameRate);
        Assert.Equal(4, loadedClips[0].Frames.Count);
        Assert.Equal(5, loadedClips[0].GetOverrideCount(0));

        // Verify metadata
        Assert.Equal("TestProject", loadedMeta.Name);
        Assert.Equal("George", loadedMeta.Author);
    }

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var serializer = CreateSerializer();
        var meta = new ProjectMetadata { Name = "Test" };

        string json = serializer.Serialize(model, CreateIndex(), [], meta);

        // Should not throw
        var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public void Serialize_OutputIsIndented()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);
        var serializer = CreateSerializer();
        var meta = new ProjectMetadata { Name = "Test" };

        string json = serializer.Serialize(model, CreateIndex(), [], meta);

        Assert.Contains("\n", json);
        Assert.Contains("  ", json);
    }

    [Fact]
    public void Deserialize_UnknownVersion_ThrowsNotSupported()
    {
        string json = """{"formatVersion": 99, "metadata": {}, "palette": [], "voxels": [], "regions": [], "animationClips": []}""";
        var serializer = CreateSerializer();

        var ex = Assert.Throws<NotSupportedException>(() => serializer.Deserialize(json));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void RoundTrip_EmptyModel_WorksCorrectly()
    {
        var model = CreateModel();
        var labels = CreateIndex();
        var meta = new ProjectMetadata { Name = "Empty" };
        var serializer = CreateSerializer();

        string json = serializer.Serialize(model, labels, [], meta);
        var (loadedModel, loadedLabels, loadedClips, loadedMeta) = serializer.Deserialize(json);

        Assert.Equal(0, loadedModel.GetVoxelCount());
        Assert.Empty(loadedClips);
        Assert.Equal("Empty", loadedMeta.Name);
    }

    [Fact]
    public void RoundTrip_NullOverride_Preserved()
    {
        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var clip = new AnimationClip(model, NullLogger<AnimationClip>.Instance) { Name = "test" };
        clip.AddFrame();
        clip.SetFrameOverride(0, new Point3(0, 0, 0), null); // Remove override

        var serializer = CreateSerializer();
        var meta = new ProjectMetadata { Name = "Test" };

        string json = serializer.Serialize(model, CreateIndex(), [clip], meta);
        var (_, _, loadedClips, _) = serializer.Deserialize(json);

        // The null override should be preserved
        var frame = loadedClips[0].Frames[0];
        Assert.True(frame.VoxelOverrides.ContainsKey(new Point3(0, 0, 0)));
        Assert.Null(frame.VoxelOverrides[new Point3(0, 0, 0)]);
    }

    [Fact]
    public void RoundTrip_MaterialMetadata_Preserved()
    {
        var model = CreateModel();
        model.Palette.Set(1, new MaterialDef
        {
            Name = "Metal",
            Color = new RgbaColor(200, 200, 210),
            Metadata = new Dictionary<string, string> { ["roughness"] = "0.3", ["metallic"] = "1.0" },
        });

        var serializer = CreateSerializer();
        var meta = new ProjectMetadata { Name = "Test" };

        string json = serializer.Serialize(model, CreateIndex(), [], meta);
        var (loadedModel, _, _, _) = serializer.Deserialize(json);

        var mat = loadedModel.Palette.Get(1)!;
        Assert.Equal("0.3", mat.Metadata["roughness"]);
        Assert.Equal("1.0", mat.Metadata["metallic"]);
    }
}
