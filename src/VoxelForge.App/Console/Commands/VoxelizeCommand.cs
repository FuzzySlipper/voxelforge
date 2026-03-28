using System.Numerics;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Commands;
using VoxelForge.App.Reference;
using VoxelForge.Core;
using VoxelForge.Core.Voxelization;

namespace VoxelForge.App.Console.Commands;

public sealed class VoxelizeCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;

    public string Name => "voxelize";
    public string[] Aliases => ["vox"];
    public string HelpText => "Convert reference model to voxels. Usage: voxelize <refIndex> <resolution> [surface|solid]";

    public VoxelizeCommand(ReferenceModelRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry;
        _loggerFactory = loggerFactory;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int refIdx) || !int.TryParse(args[1], out int resolution))
            return CommandResult.Fail("Usage: voxelize <refIndex> <resolution> [surface|solid]");

        var refModel = _registry.Get(refIdx);
        if (refModel is null)
            return CommandResult.Fail($"No reference model at index {refIdx}.");

        if (resolution < 2 || resolution > 128)
            return CommandResult.Fail("Resolution must be 2-128.");

        var mode = VoxelizeMode.Solid;
        if (args.Length >= 3 && args[2].Equals("surface", StringComparison.OrdinalIgnoreCase))
            mode = VoxelizeMode.Surface;

        // Convert ReferenceModelData meshes to TriangleMesh
        var positions = new List<Vector3>();
        var vertexColors = new List<RgbaColor>();
        var indices = new List<int>();
        foreach (var mesh in refModel.Meshes)
        {
            int baseVertex = positions.Count;
            foreach (var v in mesh.Vertices)
            {
                // Apply model transform
                var pos = new Vector3(v.PosX, v.PosY, v.PosZ) * refModel.Scale;
                pos = new Vector3(
                    pos.X + refModel.PositionX,
                    pos.Y + refModel.PositionY,
                    pos.Z + refModel.PositionZ);
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

        var service = new VoxelizeService(_loggerFactory);
        var result = service.Voxelize(triMesh, resolution, mode);

        // Copy palette entries from the voxelization result into the context model
        foreach (var (palIdx, matDef) in result.Palette.Entries)
            context.Model.Palette.Set(palIdx, matDef);

        // Apply to current model via undo stack
        var commands = new List<IEditorCommand>();
        foreach (var (pos, val) in result.Voxels)
            commands.Add(new SetVoxelCommand(context.Model, pos, val));

        if (commands.Count > 0)
        {
            context.UndoStack.Execute(new CompoundCommand(commands, $"Voxelize ({commands.Count} voxels)"));
            context.OnModelChanged?.Invoke();
        }

        string colorInfo = hasColorVariation ? $", {result.Palette.Count} colors" : "";
        return CommandResult.Ok($"Voxelized: {commands.Count} voxels at resolution {resolution} ({mode}{colorInfo})");
    }
}
