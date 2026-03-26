using Microsoft.Extensions.Logging.Abstractions;

namespace VoxelForge.Core.Tests;

public sealed class LabelIndexTests
{
    private static LabelIndex CreateIndex() => new(NullLogger<LabelIndex>.Instance);

    private static RegionDef MakeRegion(string name, RegionId? parentId = null)
    {
        return new RegionDef
        {
            Id = new RegionId(name),
            Name = name,
            ParentId = parentId,
        };
    }

    [Fact]
    public void AssignRegion_TwentyVoxels_AllReturnedByRegionQuery()
    {
        var index = CreateIndex();
        var regionId = new RegionId("right_arm");
        index.AddOrUpdateRegion(MakeRegion("right_arm"));

        var voxels = Enumerable.Range(0, 20).Select(i => new Point3(i, 0, 0)).ToList();
        index.AssignRegion(regionId, voxels);

        var result = index.GetVoxelsInRegion(regionId);
        Assert.Equal(20, result.Count);
        foreach (var v in voxels)
            Assert.Contains(v, result);
    }

    [Fact]
    public void GetRegion_ByPosition_ReturnsCorrectRegionId()
    {
        var index = CreateIndex();
        var regionId = new RegionId("right_arm");
        index.AddOrUpdateRegion(MakeRegion("right_arm"));

        var voxels = Enumerable.Range(0, 20).Select(i => new Point3(i, 0, 0)).ToList();
        index.AssignRegion(regionId, voxels);

        foreach (var v in voxels)
            Assert.Equal(regionId, index.GetRegion(v));
    }

    [Fact]
    public void GetRegion_UnlabeledVoxel_ReturnsNull()
    {
        var index = CreateIndex();
        Assert.Null(index.GetRegion(new Point3(99, 99, 99)));
    }

    [Fact]
    public void AssignRegion_ReassignVoxel_RemovesFromOldRegion()
    {
        var index = CreateIndex();
        var armId = new RegionId("right_arm");
        var handId = new RegionId("right_hand");
        index.AddOrUpdateRegion(MakeRegion("right_arm"));
        index.AddOrUpdateRegion(MakeRegion("right_hand", armId));

        var sharedVoxel = new Point3(5, 0, 0);
        index.AssignRegion(armId, [sharedVoxel, new Point3(6, 0, 0)]);

        // Reassign the shared voxel to right_hand
        index.AssignRegion(handId, [sharedVoxel]);

        // Should be in right_hand now
        Assert.Equal(handId, index.GetRegion(sharedVoxel));
        Assert.Contains(sharedVoxel, index.GetVoxelsInRegion(handId));

        // Should be removed from right_arm
        Assert.DoesNotContain(sharedVoxel, index.GetVoxelsInRegion(armId));

        // The other voxel should still be in right_arm
        Assert.Contains(new Point3(6, 0, 0), index.GetVoxelsInRegion(armId));
    }

    [Fact]
    public void RemoveFromRegion_RemovesBothIndexEntries()
    {
        var index = CreateIndex();
        var regionId = new RegionId("torso");
        index.AddOrUpdateRegion(MakeRegion("torso"));

        var pos = new Point3(1, 1, 1);
        index.AssignRegion(regionId, [pos]);

        index.RemoveFromRegion(pos);

        Assert.Null(index.GetRegion(pos));
        Assert.DoesNotContain(pos, index.GetVoxelsInRegion(regionId));
    }

    [Fact]
    public void RemoveFromRegion_UnlabeledVoxel_DoesNotThrow()
    {
        var index = CreateIndex();
        var ex = Record.Exception(() => index.RemoveFromRegion(new Point3(0, 0, 0)));
        Assert.Null(ex);
    }

    [Fact]
    public void GetAncestors_ThreeLevelHierarchy_ReturnsRootToLeaf()
    {
        var index = CreateIndex();
        var bodyId = new RegionId("body");
        var armId = new RegionId("right_arm");
        var handId = new RegionId("right_hand");

        index.AddOrUpdateRegion(MakeRegion("body"));
        index.AddOrUpdateRegion(MakeRegion("right_arm", bodyId));
        index.AddOrUpdateRegion(MakeRegion("right_hand", armId));

        var ancestors = index.GetAncestors(handId);

        Assert.Equal(3, ancestors.Count);
        Assert.Equal(bodyId, ancestors[0]);
        Assert.Equal(armId, ancestors[1]);
        Assert.Equal(handId, ancestors[2]);
    }

    [Fact]
    public void GetAncestors_NoParent_ReturnsSelf()
    {
        var index = CreateIndex();
        var id = new RegionId("root");
        index.AddOrUpdateRegion(MakeRegion("root"));

        var ancestors = index.GetAncestors(id);

        Assert.Single(ancestors);
        Assert.Equal(id, ancestors[0]);
    }

    [Fact]
    public void GetDescendants_ReturnsAllChildrenAndGrandchildren()
    {
        var index = CreateIndex();
        var bodyId = new RegionId("body");
        var armId = new RegionId("right_arm");
        var handId = new RegionId("right_hand");
        var fingerId = new RegionId("index_finger");

        index.AddOrUpdateRegion(MakeRegion("body"));
        index.AddOrUpdateRegion(MakeRegion("right_arm", bodyId));
        index.AddOrUpdateRegion(MakeRegion("right_hand", armId));
        index.AddOrUpdateRegion(MakeRegion("index_finger", handId));

        var descendants = index.GetDescendants(bodyId);

        Assert.Equal(3, descendants.Count);
        Assert.Contains(armId, descendants);
        Assert.Contains(handId, descendants);
        Assert.Contains(fingerId, descendants);
    }

    [Fact]
    public void GetDescendants_NoChildren_ReturnsEmpty()
    {
        var index = CreateIndex();
        var id = new RegionId("leaf");
        index.AddOrUpdateRegion(MakeRegion("leaf"));

        var descendants = index.GetDescendants(id);
        Assert.Empty(descendants);
    }

    [Fact]
    public void Rebuild_RestoresBothIndexes()
    {
        // Simulate save/load: create RegionDefs with pre-populated voxel sets
        var armDef = new RegionDef
        {
            Id = new RegionId("right_arm"),
            Name = "right_arm",
            Voxels = [new Point3(0, 0, 0), new Point3(1, 0, 0), new Point3(2, 0, 0)],
        };
        var legDef = new RegionDef
        {
            Id = new RegionId("right_leg"),
            Name = "right_leg",
            Voxels = [new Point3(0, 5, 0), new Point3(1, 5, 0)],
        };

        var index = CreateIndex();
        index.Rebuild([armDef, legDef]);

        // ByRegion works
        Assert.Equal(3, index.GetVoxelsInRegion(new RegionId("right_arm")).Count);
        Assert.Equal(2, index.GetVoxelsInRegion(new RegionId("right_leg")).Count);

        // ByVoxel works
        Assert.Equal(new RegionId("right_arm"), index.GetRegion(new Point3(1, 0, 0)));
        Assert.Equal(new RegionId("right_leg"), index.GetRegion(new Point3(0, 5, 0)));

        // Unlabeled voxel returns null
        Assert.Null(index.GetRegion(new Point3(99, 99, 99)));
    }

    [Fact]
    public void RemoveRegion_CleansUpAllVoxelMappings()
    {
        var index = CreateIndex();
        var regionId = new RegionId("temp");
        index.AddOrUpdateRegion(MakeRegion("temp"));
        index.AssignRegion(regionId, [new Point3(0, 0, 0), new Point3(1, 1, 1)]);

        index.RemoveRegion(regionId);

        Assert.Null(index.GetRegion(new Point3(0, 0, 0)));
        Assert.Null(index.GetRegion(new Point3(1, 1, 1)));
        Assert.Empty(index.GetVoxelsInRegion(regionId));
    }

    [Fact]
    public void AssignRegion_NonExistentRegion_Throws()
    {
        var index = CreateIndex();
        Assert.Throws<InvalidOperationException>(() =>
            index.AssignRegion(new RegionId("ghost"), [new Point3(0, 0, 0)]));
    }
}
