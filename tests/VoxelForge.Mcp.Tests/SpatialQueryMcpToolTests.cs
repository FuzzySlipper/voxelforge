using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.Core;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class SpatialQueryMcpToolTests
{
    [Fact]
    public void RegionNeighborsAndInterfaces_RespectConfiguredConnectivity()
    {
        var session = CreateSession();
        var service = new SpatialQueryService();
        AddRegion(session, "rail", new Point3(0, 0, 0));
        AddRegion(session, "face_bracket", new Point3(1, 0, 0));
        AddRegion(session, "diagonal_bracket", new Point3(1, 1, 0));
        var neighborsTool = new GetRegionNeighborsMcpTool(session, service);
        var interfaceTool = new GetInterfaceVoxelsMcpTool(session, service);

        var sixResult = neighborsTool.Invoke(JsonArguments("""{ "region_id": "rail", "connectivity": 6 }"""), CancellationToken.None);
        Assert.True(sixResult.Success);
        using (var document = JsonDocument.Parse(sixResult.Message))
        {
            Assert.Equal(1, document.RootElement.GetProperty("count").GetInt32());
            var neighbor = document.RootElement.GetProperty("neighbors")[0];
            Assert.Equal("face_bracket", neighbor.GetProperty("regionId").GetString());
            Assert.Equal(1, neighbor.GetProperty("interfacePairCount").GetInt32());
        }

        var twentySixResult = neighborsTool.Invoke(JsonArguments("""{ "region_id": "rail", "connectivity": 26 }"""), CancellationToken.None);
        Assert.True(twentySixResult.Success);
        using (var document = JsonDocument.Parse(twentySixResult.Message))
        {
            Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
            Assert.Equal("diagonal_bracket", FindNeighbor(document.RootElement.GetProperty("neighbors"), "diagonal_bracket").GetProperty("regionId").GetString());
        }

        var sixInterface = interfaceTool.Invoke(JsonArguments("""
        { "region_a": "rail", "region_b": "diagonal_bracket", "connectivity": 6 }
        """), CancellationToken.None);
        Assert.True(sixInterface.Success);
        using (var document = JsonDocument.Parse(sixInterface.Message))
            Assert.Equal(0, document.RootElement.GetProperty("interfacePairCount").GetInt32());

        var twentySixInterface = interfaceTool.Invoke(JsonArguments("""
        { "region_a": "rail", "region_b": "diagonal_bracket", "connectivity": 26 }
        """), CancellationToken.None);
        Assert.True(twentySixInterface.Success);
        using (var document = JsonDocument.Parse(twentySixInterface.Message))
        {
            Assert.Equal(1, document.RootElement.GetProperty("interfacePairCount").GetInt32());
            var pair = document.RootElement.GetProperty("pairs")[0];
            Assert.Equal(0, pair.GetProperty("a").GetProperty("x").GetInt32());
            Assert.Equal(1, pair.GetProperty("b").GetProperty("x").GetInt32());
            Assert.Equal(1, pair.GetProperty("b").GetProperty("y").GetInt32());
        }

        Assert.True(neighborsTool.IsReadOnly);
        Assert.True(interfaceTool.IsReadOnly);
    }

    [Fact]
    public void MeasureDistance_ReturnsPointCentroidAndNearestSurfaceDistances()
    {
        var session = CreateSession();
        var service = new SpatialQueryService();
        AddRegion(session, "rail", new Point3(0, 0, 0), new Point3(2, 0, 0));
        AddRegion(session, "bracket", new Point3(1, 4, 0));
        AddRegion(session, "touching", new Point3(3, 0, 0));
        var tool = new MeasureDistanceMcpTool(session, service);

        var pointResult = tool.Invoke(JsonArguments("""
        { "point_a": { "x": 0, "y": 0, "z": 0 }, "point_b": { "x": 3, "y": 4, "z": 0 } }
        """), CancellationToken.None);
        Assert.True(pointResult.Success);
        using (var document = JsonDocument.Parse(pointResult.Message))
        {
            Assert.Equal("point", document.RootElement.GetProperty("mode").GetString());
            Assert.Equal(5.0, document.RootElement.GetProperty("distance").GetDouble(), precision: 6);
        }

        var centroidResult = tool.Invoke(JsonArguments("""
        { "region_a": "rail", "region_b": "bracket", "mode": "centroid" }
        """), CancellationToken.None);
        Assert.True(centroidResult.Success);
        using (var document = JsonDocument.Parse(centroidResult.Message))
        {
            Assert.Equal("centroid", document.RootElement.GetProperty("mode").GetString());
            Assert.Equal(4.0, document.RootElement.GetProperty("distance").GetDouble(), precision: 6);
            Assert.Equal(1.0, document.RootElement.GetProperty("regionA").GetProperty("centroid").GetProperty("x").GetDouble(), precision: 6);
        }

        var nearestResult = tool.Invoke(JsonArguments("""
        { "region_a": "rail", "region_b": "touching", "mode": "nearest_surface" }
        """), CancellationToken.None);
        Assert.True(nearestResult.Success);
        using (var document = JsonDocument.Parse(nearestResult.Message))
        {
            Assert.Equal("nearest_surface", document.RootElement.GetProperty("mode").GetString());
            Assert.Equal(0.0, document.RootElement.GetProperty("distance").GetDouble(), precision: 6);
            Assert.Equal(1.0, document.RootElement.GetProperty("voxelCenterDistance").GetDouble(), precision: 6);
        }
    }

    [Fact]
    public void CrossSection_ReturnsCompactRegionTextGrid()
    {
        var session = CreateSession();
        var service = new SpatialQueryService();
        AddRegion(session, "a_rail", new Point3(0, 0, 0));
        AddRegion(session, "b_bracket", new Point3(1, 0, 0));
        session.Document.Model.SetVoxel(new Point3(0, 1, 0), 3);
        var tool = new GetCrossSectionMcpTool(session, service);

        var result = tool.Invoke(JsonArguments("""{ "axis": "z", "index": 0, "value_mode": "region" }"""), CancellationToken.None);

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(result.Message);
        Assert.Equal("z", document.RootElement.GetProperty("axis").GetString());
        Assert.Equal("x", document.RootElement.GetProperty("uAxis").GetString());
        Assert.Equal("y", document.RootElement.GetProperty("vAxis").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("occupiedCount").GetInt32());
        Assert.Equal("#.\nAB", document.RootElement.GetProperty("text").GetString());
        Assert.Equal("#.", document.RootElement.GetProperty("rows")[0].GetString());
        Assert.Equal("AB", document.RootElement.GetProperty("rows")[1].GetString());
        Assert.Equal("a_rail", FindLegendEntry(document.RootElement.GetProperty("legend"), "A").GetProperty("regionId").GetString());
        Assert.Equal("b_bracket", FindLegendEntry(document.RootElement.GetProperty("legend"), "B").GetProperty("regionId").GetString());
        Assert.Equal("unlabeled occupied voxel", FindLegendEntry(document.RootElement.GetProperty("legend"), "#").GetProperty("label").GetString());
        Assert.True(tool.IsReadOnly);
    }

    [Fact]
    public void CheckCollision_HandlesRegionsAndBoxes()
    {
        var session = CreateSession();
        var service = new SpatialQueryService();
        AddRegion(session, "rail", new Point3(0, 0, 0), new Point3(1, 0, 0));
        var tool = new CheckCollisionMcpTool(session, service);

        var hitResult = tool.Invoke(JsonArguments("""
        {
            "a": { "region_id": "rail" },
            "b": { "box": { "x1": 1, "y1": 0, "z1": 0, "x2": 5, "y2": 0, "z2": 0 } }
        }
        """), CancellationToken.None);
        Assert.True(hitResult.Success);
        using (var document = JsonDocument.Parse(hitResult.Message))
        {
            Assert.True(document.RootElement.GetProperty("collides").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("overlapVoxelCount").GetInt64());
            Assert.Equal(1, document.RootElement.GetProperty("overlapVoxels")[0].GetProperty("x").GetInt32());
        }

        var missResult = tool.Invoke(JsonArguments("""
        {
            "a": { "box": { "x1": 10, "y1": 10, "z1": 10, "x2": 11, "y2": 11, "z2": 11 } },
            "b": { "box": { "x1": 12, "y1": 10, "z1": 10, "x2": 13, "y2": 11, "z2": 11 } }
        }
        """), CancellationToken.None);
        Assert.True(missResult.Success);
        using (var document = JsonDocument.Parse(missResult.Message))
        {
            Assert.False(document.RootElement.GetProperty("collides").GetBoolean());
            Assert.Equal(0, document.RootElement.GetProperty("overlapVoxelCount").GetInt64());
        }

        Assert.True(tool.IsReadOnly);
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }

    private static void AddRegion(VoxelForgeMcpSession session, string id, params Point3[] points)
    {
        var regionId = new RegionId(id);
        session.Document.Labels.AddOrUpdateRegion(new RegionDef
        {
            Id = regionId,
            Name = id,
        });

        for (int i = 0; i < points.Length; i++)
        {
            session.Document.Model.SetVoxel(points[i], 1);
            session.Document.Labels.AssignRegion(regionId, [points[i]]);
        }
    }

    private static JsonElement JsonArguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement FindNeighbor(JsonElement neighbors, string id)
    {
        foreach (var neighbor in neighbors.EnumerateArray())
        {
            if (neighbor.GetProperty("regionId").GetString() == id)
                return neighbor;
        }

        throw new InvalidOperationException("Neighbor not found: " + id);
    }

    private static JsonElement FindLegendEntry(JsonElement legend, string symbol)
    {
        foreach (var entry in legend.EnumerateArray())
        {
            if (entry.GetProperty("symbol").GetString() == symbol)
                return entry;
        }

        throw new InvalidOperationException("Legend entry not found: " + symbol);
    }
}
