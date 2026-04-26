using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class RegionMcpToolTests
{
    [Fact]
    public void RegionMcpTools_CreateAssignQueryTreeAndDelete()
    {
        var session = CreateSession();
        var regionService = new RegionEditingService();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);
        session.Document.Model.SetVoxel(new Point3(1, 0, 0), 2);
        session.Document.Model.Palette.Set(2, new MaterialDef
        {
            Name = "metal",
            Color = new RgbaColor(10, 20, 30),
        });

        var createTool = new CreateRegionMcpTool(session, regionService);
        var assignTool = new AssignVoxelsToRegionMcpTool(session, regionService);
        var listTool = new ListRegionsMcpTool(session);
        var boundsTool = new GetRegionBoundsMcpTool(session);
        var voxelsTool = new GetRegionVoxelsMcpTool(session);
        var treeTool = new GetRegionTreeMcpTool(session);
        var deleteTool = new DeleteRegionMcpTool(session, regionService);

        Assert.True(createTool.Invoke(JsonArguments("""
        { "name": "body", "properties": { "role": "root" } }
        """), CancellationToken.None).Success);
        Assert.True(createTool.Invoke(JsonArguments("""
        { "name": "arm", "parent_id": "body", "properties": { "side": "left" } }
        """), CancellationToken.None).Success);
        Assert.True(assignTool.Invoke(JsonArguments("""
        { "region_id": "arm", "positions": [ { "x": 0, "y": 0, "z": 0 }, { "x": 1, "y": 0, "z": 0 } ] }
        """), CancellationToken.None).Success);

        using (var document = JsonDocument.Parse(listTool.Invoke(EmptyArguments(), CancellationToken.None).Message))
        {
            Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
            var arm = FindRegion(document.RootElement.GetProperty("regions"), "arm");
            Assert.Equal("body", arm.GetProperty("parentId").GetString());
            Assert.Equal(2, arm.GetProperty("voxelCount").GetInt32());
            Assert.Equal("left", arm.GetProperty("properties").GetProperty("side").GetString());
            Assert.Equal("body", arm.GetProperty("ancestorIds")[0].GetString());
        }

        using (var document = JsonDocument.Parse(boundsTool.Invoke(JsonArguments("""{ "region_id": "arm" }"""), CancellationToken.None).Message))
        {
            var bounds = document.RootElement.GetProperty("bounds");
            Assert.Equal(0, bounds.GetProperty("min").GetProperty("x").GetInt32());
            Assert.Equal(1, bounds.GetProperty("max").GetProperty("x").GetInt32());
        }

        using (var document = JsonDocument.Parse(voxelsTool.Invoke(JsonArguments("""{ "region_id": "arm" }"""), CancellationToken.None).Message))
        {
            Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("metal", document.RootElement.GetProperty("voxels")[1].GetProperty("materialName").GetString());
        }

        using (var document = JsonDocument.Parse(treeTool.Invoke(EmptyArguments(), CancellationToken.None).Message))
        {
            var root = document.RootElement.GetProperty("roots")[0];
            Assert.Equal("body", root.GetProperty("id").GetString());
            Assert.Equal("arm", root.GetProperty("children")[0].GetProperty("id").GetString());
        }

        var parentDeleteResult = deleteTool.Invoke(JsonArguments("""{ "region_id": "body" }"""), CancellationToken.None);
        Assert.False(parentDeleteResult.Success);
        Assert.Contains("child region", parentDeleteResult.Message, StringComparison.Ordinal);

        var childDeleteResult = deleteTool.Invoke(JsonArguments("""{ "region_id": "arm" }"""), CancellationToken.None);
        Assert.True(childDeleteResult.Success);
        Assert.Equal((byte)1, session.Document.Model.GetVoxel(new Point3(0, 0, 0)));
        Assert.Null(session.Document.Labels.GetRegion(new Point3(0, 0, 0)));

        session.UndoStack.Undo();
        Assert.Equal(new RegionId("arm"), session.Document.Labels.GetRegion(new Point3(0, 0, 0)));
    }

    [Fact]
    public void AssignVoxelsToRegionMcpTool_AssignsExistingVoxelsFromBox()
    {
        var session = CreateSession();
        var regionService = new RegionEditingService();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);
        session.Document.Model.SetVoxel(new Point3(1, 0, 0), 1);
        session.Document.Model.SetVoxel(new Point3(3, 0, 0), 1);
        var createTool = new CreateRegionMcpTool(session, regionService);
        var assignTool = new AssignVoxelsToRegionMcpTool(session, regionService);
        var boundsTool = new GetRegionBoundsMcpTool(session);

        Assert.True(createTool.Invoke(JsonArguments("""{ "name": "rail" }"""), CancellationToken.None).Success);
        var assignResult = assignTool.Invoke(JsonArguments("""
        { "region_id": "rail", "box": { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 0, "z2": 0 } }
        """), CancellationToken.None);

        Assert.True(assignResult.Success);
        Assert.Equal(2, session.Document.Labels.Regions[new RegionId("rail")].Voxels.Count);
        Assert.Null(session.Document.Labels.GetRegion(new Point3(3, 0, 0)));

        using var document = JsonDocument.Parse(boundsTool.Invoke(JsonArguments("""{ "region_id": "rail" }"""), CancellationToken.None).Message);
        Assert.Equal(1, document.RootElement.GetProperty("bounds").GetProperty("max").GetProperty("x").GetInt32());
    }

    [Fact]
    public void RegionsCreatedThroughMcpTools_SurviveSaveLoadCycle()
    {
        var path = Path.Combine(Path.GetTempPath(), "voxelforge-region-mcp-" + Guid.NewGuid().ToString("N") + ".vforge");
        try
        {
            var session = CreateSession();
            var regionService = new RegionEditingService();
            session.Document.Model.SetVoxel(new Point3(4, 5, 6), 8);
            var createTool = new CreateRegionMcpTool(session, regionService);
            var assignTool = new AssignVoxelsToRegionMcpTool(session, regionService);

            Assert.True(createTool.Invoke(JsonArguments("""{ "name": "body" }"""), CancellationToken.None).Success);
            Assert.True(createTool.Invoke(JsonArguments("""
            { "name": "hand", "parent_id": "body", "properties": { "side": "right" } }
            """), CancellationToken.None).Success);
            Assert.True(assignTool.Invoke(JsonArguments("""
            { "region_id": "hand", "positions": [ { "x": 4, "y": 5, "z": 6 } ] }
            """), CancellationToken.None).Success);

            var lifecycleService = new ProjectLifecycleService(NullLoggerFactory.Instance);
            var saveResult = lifecycleService.Save(session.Document, session.Events, new SaveProjectRequest(path));
            Assert.True(saveResult.Success);

            var loadedSession = CreateSession();
            var loadResult = lifecycleService.Load(loadedSession.Document, loadedSession.UndoStack, loadedSession.Events, new LoadProjectRequest(path));
            Assert.True(loadResult.Success);

            var loadedRegion = loadedSession.Document.Labels.Regions[new RegionId("hand")];
            Assert.Equal(new RegionId("body"), loadedRegion.ParentId);
            Assert.Equal("right", loadedRegion.Properties["side"]);
            Assert.Equal(new RegionId("hand"), loadedSession.Document.Labels.GetRegion(new Point3(4, 5, 6)));
            Assert.Equal((byte)8, loadedSession.Document.Model.GetVoxel(new Point3(4, 5, 6)));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }

    private static JsonElement EmptyArguments()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    private static JsonElement JsonArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement FindRegion(JsonElement regions, string id)
    {
        foreach (var region in regions.EnumerateArray())
        {
            if (region.GetProperty("id").GetString() == id)
                return region;
        }

        throw new InvalidOperationException("Region not found: " + id);
    }
}
