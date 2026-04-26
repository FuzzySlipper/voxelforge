using System.Numerics;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Reference;
using VoxelForge.Core;
using VoxelForge.Core.Voxelization;

namespace VoxelForge.App.Console.Commands;

/// <summary>
/// Voxelizes the same reference model at multiple resolutions, placed side by side
/// for visual comparison.
/// </summary>
public sealed class VoxelizeCompareCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "voxcompare";
    public string[] Aliases => ["vcomp"];
    public string HelpText => "Compare voxelizations. Usage: voxcompare <refIndex> <res1,res2,...> [surface|solid]";

    public VoxelizeCompareCommand(ReferenceModelState referenceModelState, ILoggerFactory loggerFactory)
    {
        _referenceModelState = referenceModelState;
        _loggerFactory = loggerFactory;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2)
            return CommandResult.Fail(HelpText);

        if (!int.TryParse(args[0], out int refIdx))
            return CommandResult.Fail("Invalid model index.");

        var refModel = _referenceModelState.Get(refIdx);
        if (refModel is null)
            return CommandResult.Fail($"No reference model at index {refIdx}.");

        var resParts = args[1].Split(',');
        var resolutions = new List<int>();
        foreach (var part in resParts)
        {
            if (!int.TryParse(part.Trim(), out int r) || r < 2 || r > 256)
                return CommandResult.Fail($"Invalid resolution: {part}. Must be 2-256.");
            resolutions.Add(r);
        }

        if (resolutions.Count < 2)
            return CommandResult.Fail("Provide at least 2 resolutions to compare.");

        var mode = VoxelizeMode.Solid;
        if (args.Length >= 3 && args[2].Equals("surface", StringComparison.OrdinalIgnoreCase))
            mode = VoxelizeMode.Surface;

        // Build transform (same as VoxelizeCommand)
        var transform = Matrix4x4.CreateScale(refModel.Scale)
            * Matrix4x4.CreateFromYawPitchRoll(
                float.DegreesToRadians(refModel.RotationY),
                float.DegreesToRadians(refModel.RotationX),
                float.DegreesToRadians(refModel.RotationZ))
            * Matrix4x4.CreateTranslation(refModel.PositionX, refModel.PositionY, refModel.PositionZ);

        // Extract triangles
        var positions = new List<Vector3>();
        var vertexColors = new List<RgbaColor>();
        var indices = new List<int>();
        foreach (var mesh in refModel.Meshes)
        {
            int baseVertex = positions.Count;
            foreach (var v in mesh.Vertices)
            {
                var pos = Vector3.Transform(new Vector3(v.PosX, v.PosY, v.PosZ), transform);
                positions.Add(pos);
                vertexColors.Add(new RgbaColor(v.R, v.G, v.B, v.A));
            }
            foreach (var idx in mesh.Indices)
                indices.Add(baseVertex + idx);
        }

        bool hasColorVariation = false;
        if (vertexColors.Count > 1)
        {
            var first = vertexColors[0];
            for (int i = 1; i < vertexColors.Count; i++)
            {
                if (vertexColors[i] != first) { hasColorVariation = true; break; }
            }
        }

        var triMesh = new TriangleMesh
        {
            Positions = positions.ToArray(),
            Indices = indices.ToArray(),
            VertexColors = hasColorVariation ? vertexColors.ToArray() : null,
        };

        var aabbMin = positions[0];
        var aabbMax = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            aabbMin = Vector3.Min(aabbMin, positions[i]);
            aabbMax = Vector3.Max(aabbMax, positions[i]);
        }
        var aabbSize = aabbMax - aabbMin;
        float maxDim = MathF.Max(aabbSize.X, MathF.Max(aabbSize.Y, aabbSize.Z));

        // Voxelize at each resolution and collect results offset along X
        var allVoxels = new Dictionary<Point3, byte>();
        var service = new VoxelizeService(_loggerFactory);
        int xOffset = 0;
        int padding = (int)MathF.Ceiling(maxDim * 0.2f) + 2;
        var summaries = new List<string>();

        foreach (int resolution in resolutions)
        {
            float cellSize = maxDim / resolution;
            var result = service.Voxelize(triMesh, resolution, mode);

            // Copy palette entries
            foreach (var (palIdx, matDef) in result.Palette.Entries)
                context.Model.Palette.Set(palIdx, matDef);
            if (result.Palette.Count > 0)
            {
                context.Events.Publish(new PaletteChangedEvent(
                    PaletteChangeKind.EntriesChanged,
                    "Copied comparison voxelization palette entries",
                    null,
                    result.Palette.Count));
            }

            // Remap to world-integer space with X offset
            var gridMin = aabbMin - new Vector3(cellSize * 0.5f);
            int count = 0;
            foreach (var (gridPos, val) in result.Voxels)
            {
                int wx = (int)MathF.Floor(gridMin.X + gridPos.X * cellSize) + xOffset;
                int wy = (int)MathF.Floor(gridMin.Y + gridPos.Y * cellSize);
                int wz = (int)MathF.Floor(gridMin.Z + gridPos.Z * cellSize);
                allVoxels[new Point3(wx, wy, wz)] = val;
                count++;
            }

            summaries.Add($"res {resolution}: {count} voxels");

            // Advance X offset for next comparison
            int modelWidth = (int)MathF.Ceiling(aabbSize.X) + 1;
            if (modelWidth < resolution) modelWidth = resolution;
            xOffset += modelWidth + padding;
        }

        // Build compound command to replace the model
        var commands = new List<IEditorCommand>();
        foreach (var pos in context.Model.Voxels.Keys)
        {
            if (!allVoxels.ContainsKey(pos))
                commands.Add(new RemoveVoxelCommand(context.Model, pos));
        }
        foreach (var (pos, val) in allVoxels)
            commands.Add(new SetVoxelCommand(context.Model, pos, val));

        if (commands.Count > 0)
        {
            context.UndoStack.Execute(new CompoundCommand(commands,
                $"Voxelize compare ({resolutions.Count} resolutions)"));
            context.Events.Publish(new VoxelModelChangedEvent(
                VoxelModelChangeKind.VoxelizeComparison,
                $"Voxelized {resolutions.Count} comparison resolution(s)",
                allVoxels.Count));
        }

        return CommandResult.Ok(
            $"Comparison: {string.Join(", ", summaries)}\n" +
            $"  Placed side by side along X axis. Total: {allVoxels.Count} voxels.");
    }
}
