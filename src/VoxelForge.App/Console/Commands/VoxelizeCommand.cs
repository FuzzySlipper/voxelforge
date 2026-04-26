using System.Numerics;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Commands;
using VoxelForge.App.Reference;
using VoxelForge.Core;
using VoxelForge.Core.Voxelization;

namespace VoxelForge.App.Console.Commands;

public sealed class VoxelizeCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "voxelize";
    public string[] Aliases => ["vox"];
    public string HelpText => "Convert reference model to voxels. Usage: voxelize <refIndex> <resolution> [surface|solid]";

    public VoxelizeCommand(ReferenceModelState referenceModelState, ILoggerFactory loggerFactory)
    {
        _referenceModelState = referenceModelState;
        _loggerFactory = loggerFactory;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int refIdx) || !int.TryParse(args[1], out int resolution))
            return CommandResult.Fail("Usage: voxelize <refIndex> <resolution> [surface|solid]");

        var refModel = _referenceModelState.Get(refIdx);
        if (refModel is null)
            return CommandResult.Fail($"No reference model at index {refIdx}.");

        if (resolution < 2 || resolution > 256)
            return CommandResult.Fail("Resolution must be 2-256.");

        var mode = VoxelizeMode.Solid;
        if (args.Length >= 3 && args[2].Equals("surface", StringComparison.OrdinalIgnoreCase))
            mode = VoxelizeMode.Surface;

        // Build the same Scale → YawPitchRoll → Translation transform the renderer uses
        var transform = Matrix4x4.CreateScale(refModel.Scale)
            * Matrix4x4.CreateFromYawPitchRoll(
                float.DegreesToRadians(refModel.RotationY),
                float.DegreesToRadians(refModel.RotationX),
                float.DegreesToRadians(refModel.RotationZ))
            * Matrix4x4.CreateTranslation(refModel.PositionX, refModel.PositionY, refModel.PositionZ);

        // Convert ReferenceModelData meshes to TriangleMesh
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

        // Check if vertex colors are diverse (texture-baked or meaningful vertex colors)
        // If all vertices have the same color, skip color voxelization
        bool hasColorVariation = false;
        if (vertexColors.Count > 1)
        {
            var first = vertexColors[0];
            for (int i = 1; i < vertexColors.Count; i++)
            {
                if (vertexColors[i] != first)
                {
                    hasColorVariation = true;
                    break;
                }
            }
        }

        var triMesh = new TriangleMesh
        {
            Positions = positions.ToArray(),
            Indices = indices.ToArray(),
            VertexColors = hasColorVariation ? vertexColors.ToArray() : null,
        };

        // Compute AABB of transformed positions for world-space remapping
        var aabbMin = positions[0];
        var aabbMax = positions[0];
        for (int i = 1; i < positions.Count; i++)
        {
            aabbMin = Vector3.Min(aabbMin, positions[i]);
            aabbMax = Vector3.Max(aabbMax, positions[i]);
        }
        var aabbSize = aabbMax - aabbMin;
        float maxDim = MathF.Max(aabbSize.X, MathF.Max(aabbSize.Y, aabbSize.Z));
        float cellSize = maxDim / resolution;

        var service = new VoxelizeService(_loggerFactory);
        var result = service.Voxelize(triMesh, resolution, mode);

        // Copy palette entries from the voxelization result into the context model
        foreach (var (palIdx, matDef) in result.Palette.Entries)
            context.Model.Palette.Set(palIdx, matDef);

        // Remap voxel positions from grid space [0, resolution) to world-integer space
        // so the model's scale and position are reflected in the output
        var gridMin = aabbMin - new Vector3(cellSize * 0.5f); // matches voxelizer padding
        var newVoxels = new Dictionary<Point3, byte>();
        foreach (var (gridPos, val) in result.Voxels)
        {
            int wx = (int)MathF.Floor(gridMin.X + gridPos.X * cellSize);
            int wy = (int)MathF.Floor(gridMin.Y + gridPos.Y * cellSize);
            int wz = (int)MathF.Floor(gridMin.Z + gridPos.Z * cellSize);
            newVoxels[new Point3(wx, wy, wz)] = val;
        }

        // Build a single compound command that replaces the entire model:
        // 1) Remove existing voxels not present in the new result
        // 2) Set all new voxels (SetVoxelCommand handles overwrite via undo)
        var commands = new List<IEditorCommand>();
        foreach (var pos in context.Model.Voxels.Keys)
        {
            if (!newVoxels.ContainsKey(pos))
                commands.Add(new RemoveVoxelCommand(context.Model, pos));
        }
        foreach (var (pos, val) in newVoxels)
            commands.Add(new SetVoxelCommand(context.Model, pos, val));

        if (commands.Count > 0)
        {
            context.UndoStack.Execute(new CompoundCommand(commands, $"Voxelize ({newVoxels.Count} voxels)"));
            context.OnModelChanged?.Invoke();
        }

        int removed = commands.Count - newVoxels.Count;
        string removeInfo = removed > 0 ? $", {removed} removed" : "";
        string colorInfo = hasColorVariation ? $", {result.Palette.Count} colors" : "";
        return CommandResult.Ok($"Voxelized: {newVoxels.Count} voxels at resolution {resolution} ({mode}{colorInfo}{removeInfo})");
    }
}
