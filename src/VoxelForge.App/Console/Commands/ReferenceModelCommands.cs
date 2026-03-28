using Microsoft.Extensions.Logging;
using VoxelForge.App.Reference;
using VoxelForge.Content;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Console.Commands;

public sealed class RefLoadCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;
    private readonly ReferenceModelLoader _loader;

    public string Name => "refload";
    public string[] Aliases => [];
    public string HelpText => "Load a reference model. Usage: refload <filepath>";

    public RefLoadCommand(ReferenceModelRegistry registry, ReferenceModelLoader loader)
    {
        _registry = registry;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: refload <filepath>");

        try
        {
            var model = _loader.Load(args[0]);
            _registry.Add(model);
            _registry.NotifyChanged();
            int idx = _registry.Models.Count - 1;
            return CommandResult.Ok(
                $"Loaded [{idx}] {model.Format} — {model.Meshes.Count} meshes, {model.TotalVertices} vertices, {model.TotalTriangles} triangles");
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Failed to load: {ex.Message}");
        }
    }
}

public sealed class RefListCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;

    public string Name => "reflist";
    public string[] Aliases => [];
    public string HelpText => "List loaded reference models.";

    public RefListCommand(ReferenceModelRegistry registry) => _registry = registry;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (_registry.Models.Count == 0)
            return CommandResult.Ok("No reference models loaded.");

        var lines = new List<string>();
        for (int i = 0; i < _registry.Models.Count; i++)
        {
            var m = _registry.Models[i];
            var vis = m.IsVisible ? "visible" : "hidden";
            lines.Add($"  [{i}] {Path.GetFileName(m.FilePath)} — {m.Format}, {m.TotalVertices} verts, {m.RenderMode}, {vis}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class RefRemoveCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;

    public string Name => "refremove";
    public string[] Aliases => [];
    public string HelpText => "Remove a reference model. Usage: refremove <index>";

    public RefRemoveCommand(ReferenceModelRegistry registry) => _registry = registry;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refremove <index>");

        if (_registry.Get(idx) is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        _registry.RemoveAt(idx);
        _registry.NotifyChanged();
        return CommandResult.Ok($"Removed reference model [{idx}].");
    }
}

public sealed class RefTransformCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;

    public string Name => "reftransform";
    public string[] Aliases => ["refmove"];
    public string HelpText => "Transform a reference model. Usage: reftransform <index> <x> <y> <z> [rx] [ry] [rz] [scale]";

    public RefTransformCommand(ReferenceModelRegistry registry) => _registry = registry;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: reftransform <index> <x> <y> <z> [rx] [ry] [rz] [scale]");

        var model = _registry.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        if (!float.TryParse(args[1], out float x) || !float.TryParse(args[2], out float y) ||
            !float.TryParse(args[3], out float z))
            return CommandResult.Fail("Invalid position values.");

        model.PositionX = x;
        model.PositionY = y;
        model.PositionZ = z;

        if (args.Length >= 7 && float.TryParse(args[4], out float rx) &&
            float.TryParse(args[5], out float ry) && float.TryParse(args[6], out float rz))
        {
            model.RotationX = rx;
            model.RotationY = ry;
            model.RotationZ = rz;
        }

        if (args.Length >= 8 && float.TryParse(args[7], out float scale))
            model.Scale = scale;

        return CommandResult.Ok($"[{idx}] pos=({x},{y},{z}) rot=({model.RotationX},{model.RotationY},{model.RotationZ}) scale={model.Scale}");
    }
}

public sealed class RefModeCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;

    public string Name => "refmode";
    public string[] Aliases => [];
    public string HelpText => "Set render mode. Usage: refmode <index> <wireframe|solid|transparent>";

    public RefModeCommand(ReferenceModelRegistry registry) => _registry = registry;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refmode <index> <wireframe|solid|transparent>");

        var model = _registry.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        if (!Enum.TryParse<ReferenceRenderMode>(args[1], ignoreCase: true, out var mode))
            return CommandResult.Fail("Invalid mode. Use: wireframe, solid, or transparent.");

        model.RenderMode = mode;
        return CommandResult.Ok($"[{idx}] render mode = {mode}");
    }
}

public sealed class RefVisibilityCommand : IConsoleCommand
{
    private readonly ReferenceModelRegistry _registry;
    private readonly bool _show;

    public string Name => _show ? "refshow" : "refhide";
    public string[] Aliases => [];
    public string HelpText => _show ? "Show a reference model. Usage: refshow <index>" : "Hide a reference model. Usage: refhide <index>";

    public RefVisibilityCommand(ReferenceModelRegistry registry, bool show)
    {
        _registry = registry;
        _show = show;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail($"Usage: {Name} <index>");

        var model = _registry.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        model.IsVisible = _show;
        return CommandResult.Ok($"[{idx}] {(_show ? "shown" : "hidden")}");
    }
}
