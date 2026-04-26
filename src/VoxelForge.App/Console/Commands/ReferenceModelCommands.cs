using Microsoft.Extensions.Logging;
using VoxelForge.App.Reference;
using VoxelForge.Content;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Console.Commands;

public sealed class RefLoadCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceModelLoader _loader;

    public string Name => "refload";
    public string[] Aliases => [];
    public string HelpText => "Load a reference model. Usage: refload <filepath>";

    public RefLoadCommand(ReferenceModelState referenceModelState, ReferenceModelLoader loader)
    {
        _referenceModelState = referenceModelState;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail("Usage: refload <filepath>");

        try
        {
            var model = _loader.Load(args[0]);
            _referenceModelState.Add(model);
            _referenceModelState.NotifyChanged();
            int idx = _referenceModelState.Models.Count - 1;
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
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "reflist";
    public string[] Aliases => [];
    public string HelpText => "List loaded reference models.";

    public RefListCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (_referenceModelState.Models.Count == 0)
            return CommandResult.Ok("No reference models loaded.");

        var lines = new List<string>();
        for (int i = 0; i < _referenceModelState.Models.Count; i++)
        {
            var m = _referenceModelState.Models[i];
            var vis = m.IsVisible ? "visible" : "hidden";
            lines.Add($"  [{i}] {Path.GetFileName(m.FilePath)} — {m.Format}, {m.TotalVertices} verts, {m.RenderMode}, {vis}");
        }

        return CommandResult.Ok(string.Join("\n", lines));
    }
}

public sealed class RefRemoveCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refremove";
    public string[] Aliases => [];
    public string HelpText => "Remove a reference model. Usage: refremove <index>";

    public RefRemoveCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refremove <index>");

        if (_referenceModelState.Get(idx) is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        _referenceModelState.RemoveAt(idx);
        _referenceModelState.NotifyChanged();
        return CommandResult.Ok($"Removed reference model [{idx}].");
    }
}

public sealed class RefClearCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refclear";
    public string[] Aliases => [];
    public string HelpText => "Remove all loaded reference models.";

    public RefClearCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        int count = _referenceModelState.Models.Count;
        if (count == 0)
            return CommandResult.Ok("No reference models to remove.");

        _referenceModelState.Clear();
        _referenceModelState.NotifyChanged();
        return CommandResult.Ok($"Removed {count} reference model(s).");
    }
}

public sealed class RefTransformCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "reftransform";
    public string[] Aliases => ["refmove"];
    public string HelpText => "Transform a reference model. Usage: reftransform <index> <x> <y> <z> [rx] [ry] [rz] [scale]";

    public RefTransformCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 4 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: reftransform <index> <x> <y> <z> [rx] [ry] [rz] [scale]");

        var model = _referenceModelState.Get(idx);
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
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refmode";
    public string[] Aliases => [];
    public string HelpText => "Set render mode. Usage: refmode <index> <wireframe|solid|transparent>";

    public RefModeCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refmode <index> <wireframe|solid|transparent>");

        var model = _referenceModelState.Get(idx);
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
    private readonly ReferenceModelState _referenceModelState;
    private readonly bool _show;

    public string Name => _show ? "refshow" : "refhide";
    public string[] Aliases => [];
    public string HelpText => _show ? "Show a reference model. Usage: refshow <index>" : "Hide a reference model. Usage: refhide <index>";

    public RefVisibilityCommand(ReferenceModelState referenceModelState, bool show)
    {
        _referenceModelState = referenceModelState;
        _show = show;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail($"Usage: {Name} <index>");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        model.IsVisible = _show;
        return CommandResult.Ok($"[{idx}] {(_show ? "shown" : "hidden")}");
    }
}

public sealed class RefScaleCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refscale";
    public string[] Aliases => [];
    public string HelpText => "Set scale of a reference model. Usage: refscale <index> <scale>";

    public RefScaleCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int idx) || !float.TryParse(args[1], out float scale))
            return CommandResult.Fail("Usage: refscale <index> <scale>");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        model.Scale = scale;
        return CommandResult.Ok($"[{idx}] scale = {scale}");
    }
}

public sealed class RefRotateCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refrotate";
    public string[] Aliases => ["refrot"];
    public string HelpText => "Quick rotate a reference model on one axis. Usage: refrotate <index> <x|y|z> [degrees=90]";

    public RefRotateCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refrotate <index> <x|y|z> [degrees=90]");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        float degrees = 90f;
        if (args.Length >= 3 && !float.TryParse(args[2], out degrees))
            return CommandResult.Fail("Invalid degrees value.");

        switch (args[1].ToLowerInvariant())
        {
            case "x": model.RotationX += degrees; break;
            case "y": model.RotationY += degrees; break;
            case "z": model.RotationZ += degrees; break;
            default: return CommandResult.Fail("Axis must be x, y, or z.");
        }

        return CommandResult.Ok($"[{idx}] rot=({model.RotationX},{model.RotationY},{model.RotationZ})");
    }
}

public sealed class RefOrientCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "reforient";
    public string[] Aliases => ["refautopose"];
    public string HelpText => "Auto-orient a reference model: upright (Y+), feet at Y=0, facing Z+. Usage: reforient <index>";

    public RefOrientCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: reforient <index>");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        // Compute the axis-aligned bounding box of the raw mesh data
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                if (v.PosX < minX) minX = v.PosX;
                if (v.PosY < minY) minY = v.PosY;
                if (v.PosZ < minZ) minZ = v.PosZ;
                if (v.PosX > maxX) maxX = v.PosX;
                if (v.PosY > maxY) maxY = v.PosY;
                if (v.PosZ > maxZ) maxZ = v.PosZ;
            }
        }

        if (minX == float.MaxValue)
            return CommandResult.Fail("Model has no vertices.");

        float extX = maxX - minX;
        float extY = maxY - minY;
        float extZ = maxZ - minZ;

        // Determine which raw axis is tallest (should become Y-up)
        // and which of the remaining two is widest (should become X, leaving the other as Z-forward)
        float rx = 0, ry = 0, rz = 0;

        if (extY >= extX && extY >= extZ)
        {
            // Already Y-tallest — no rotation needed for uprighting
            // Check if the model is wider in X or Z for facing
            // We want the narrower horizontal axis to be Z (depth/forward)
            if (extX < extZ)
                ry = 90; // rotate so the narrow axis faces Z+
        }
        else if (extZ >= extX && extZ >= extY)
        {
            // Z is tallest — rotate to make it Y-up
            // Pitch -90°: +Z maps to +Y (right-hand rule: positive pitch sends +Z to -Y)
            rx = -90;
        }
        else
        {
            // X is tallest — rotate to make it Y-up
            // Roll +90°: +X maps to +Y (positive roll sends +X to +Y)
            rz = 90;
        }

        model.RotationX = rx;
        model.RotationY = ry;
        model.RotationZ = rz;

        // After rotation, recompute the transformed bounding box to position feet at Y=0
        // Apply the rotation matrix to all vertices to find the new min Y
        float cosRx = MathF.Cos(MathF.PI / 180f * rx);
        float sinRx = MathF.Sin(MathF.PI / 180f * rx);
        float cosRy = MathF.Cos(MathF.PI / 180f * ry);
        float sinRy = MathF.Sin(MathF.PI / 180f * ry);
        float cosRz = MathF.Cos(MathF.PI / 180f * rz);
        float sinRz = MathF.Sin(MathF.PI / 180f * rz);

        float newMinY = float.MaxValue;
        float newMinX = float.MaxValue, newMaxX = float.MinValue;
        float newMinZ = float.MaxValue, newMaxZ = float.MinValue;

        foreach (var mesh in model.Meshes)
        {
            foreach (var v in mesh.Vertices)
            {
                float px = v.PosX * model.Scale;
                float py = v.PosY * model.Scale;
                float pz = v.PosZ * model.Scale;

                // Apply YawPitchRoll (Y, X, Z) matching the renderer's CreateFromYawPitchRoll order
                // Yaw (around Y)
                float x1 = cosRy * px + sinRy * pz;
                float y1 = py;
                float z1 = -sinRy * px + cosRy * pz;
                // Pitch (around X)
                float x2 = x1;
                float y2 = cosRx * y1 - sinRx * z1;
                float z2 = sinRx * y1 + cosRx * z1;
                // Roll (around Z)
                float x3 = cosRz * x2 - sinRz * y2;
                float y3 = sinRz * x2 + cosRz * y2;
                float z3 = z2;

                if (y3 < newMinY) newMinY = y3;
                if (x3 < newMinX) newMinX = x3;
                if (x3 > newMaxX) newMaxX = x3;
                if (z3 < newMinZ) newMinZ = z3;
                if (z3 > newMaxZ) newMaxZ = z3;
            }
        }

        // Position so bottom is at Y=0, centered on X and Z
        model.PositionY = -newMinY;
        model.PositionX = -(newMinX + newMaxX) / 2f;
        model.PositionZ = -(newMinZ + newMaxZ) / 2f;

        return CommandResult.Ok(
            $"[{idx}] oriented: pos=({model.PositionX:F1},{model.PositionY:F1},{model.PositionZ:F1}) rot=({rx},{ry},{rz}) scale={model.Scale}");
    }
}

public sealed class RefInfoCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceModelLoader _loader;

    public string Name => "refinfo";
    public string[] Aliases => [];
    public string HelpText => "Inspect material/texture/UV info for a loaded reference model. Usage: refinfo <index>";

    public RefInfoCommand(ReferenceModelState referenceModelState, ReferenceModelLoader loader)
    {
        _referenceModelState = referenceModelState;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refinfo <index>");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        try
        {
            var info = _loader.Inspect(model.FilePath);
            return CommandResult.Ok(info);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Inspect failed: {ex.Message}");
        }
    }
}

public sealed class RefAnimCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refanim";
    public string[] Aliases => [];
    public string HelpText => "Control reference model animation. Usage: refanim <index> <list|play|stop|pause|frame|speed> [args]";

    public RefAnimCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2 || !int.TryParse(args[0], out int idx))
            return CommandResult.Fail("Usage: refanim <index> <list|play|stop|pause|frame|speed> [args]");

        var model = _referenceModelState.Get(idx);
        if (model is null)
            return CommandResult.Fail($"No reference model at index {idx}.");

        var action = args[1].ToLowerInvariant();

        return action switch
        {
            "list" => ListClips(model, idx),
            "play" => Play(model, idx, args),
            "stop" => Stop(model, idx),
            "pause" => Pause(model, idx),
            "frame" => Scrub(model, idx, args),
            "speed" => SetSpeed(model, idx, args),
            _ => CommandResult.Fail($"Unknown action '{action}'. Use: list, play, stop, pause, frame, speed."),
        };
    }

    private static CommandResult ListClips(ReferenceModelData model, int idx)
    {
        if (!model.HasAnimations)
            return CommandResult.Ok($"[{idx}] No animations found in this model.");

        var lines = new List<string> { $"[{idx}] {model.AnimationClips!.Count} animation clip(s):" };
        for (int i = 0; i < model.AnimationClips.Count; i++)
        {
            var clip = model.AnimationClips[i];
            lines.Add($"  [{i}] \"{clip.Name}\" — {clip.Duration:F2}s, {clip.Channels.Count} channels");
        }

        if (model.Skeleton is not null)
            lines.Add($"  Skeleton: {model.Skeleton.BoneCount} bones");

        return CommandResult.Ok(string.Join("\n", lines));
    }

    private static CommandResult Play(ReferenceModelData model, int idx, string[] args)
    {
        if (!model.HasAnimations)
            return CommandResult.Fail($"[{idx}] No animations to play.");

        int clipIdx = 0;
        if (args.Length >= 3)
        {
            // Try as index first, then as name
            if (int.TryParse(args[2], out int parsed))
            {
                clipIdx = parsed;
            }
            else
            {
                var name = args[2];
                clipIdx = -1;
                for (int i = 0; i < model.AnimationClips!.Count; i++)
                {
                    if (model.AnimationClips[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        clipIdx = i;
                        break;
                    }
                }
                if (clipIdx < 0)
                    return CommandResult.Fail($"No clip named '{name}'. Use 'refanim {idx} list' to see available clips.");
            }
        }

        if (clipIdx < 0 || clipIdx >= model.AnimationClips!.Count)
            return CommandResult.Fail($"Clip index {clipIdx} out of range (0-{model.AnimationClips!.Count - 1}).");

        model.ActiveClipIndex = clipIdx;
        model.AnimationTime = 0;
        model.IsAnimating = true;

        var clip = model.AnimationClips[clipIdx];
        return CommandResult.Ok($"[{idx}] Playing \"{clip.Name}\" ({clip.Duration:F2}s)");
    }

    private static CommandResult Stop(ReferenceModelData model, int idx)
    {
        model.IsAnimating = false;
        model.ActiveClipIndex = null;
        model.AnimationTime = 0;
        return CommandResult.Ok($"[{idx}] Stopped, reset to bind pose.");
    }

    private static CommandResult Pause(ReferenceModelData model, int idx)
    {
        model.IsAnimating = false;
        return CommandResult.Ok($"[{idx}] Paused at {model.AnimationTime:F2}s.");
    }

    private static CommandResult Scrub(ReferenceModelData model, int idx, string[] args)
    {
        if (!model.HasAnimations)
            return CommandResult.Fail($"[{idx}] No animations.");

        if (args.Length < 3 || !float.TryParse(args[2], out float time))
            return CommandResult.Fail("Usage: refanim <index> frame <time_seconds>");

        // Ensure a clip is active
        model.ActiveClipIndex ??= 0;
        model.AnimationTime = Math.Max(0, time);
        model.IsAnimating = false; // Pause at this frame

        return CommandResult.Ok($"[{idx}] Scrubbed to {model.AnimationTime:F2}s (paused).");
    }

    private static CommandResult SetSpeed(ReferenceModelData model, int idx, string[] args)
    {
        if (args.Length < 3 || !float.TryParse(args[2], out float speed))
            return CommandResult.Fail("Usage: refanim <index> speed <multiplier>");

        model.AnimationSpeed = speed;
        return CommandResult.Ok($"[{idx}] Animation speed = {speed:F1}x");
    }
}

public sealed class RefTexCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceModelLoader _loader;

    public string Name => "reftex";
    public string[] Aliases => [];
    public string HelpText => "Swap texture on a reference model. Usage: reftex <modelIndex> <texturePath> [meshIndex]\n" +
        "  Omit meshIndex to apply to all meshes.";

    public RefTexCommand(ReferenceModelState referenceModelState, ReferenceModelLoader loader)
    {
        _referenceModelState = referenceModelState;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2)
            return CommandResult.Fail(HelpText);

        if (!int.TryParse(args[0], out int modelIdx))
            return CommandResult.Fail("Invalid model index.");

        var model = _referenceModelState.Get(modelIdx);
        if (model is null)
            return CommandResult.Fail($"No model at index {modelIdx}.");

        string texPath = args[1];
        if (!File.Exists(texPath))
            return CommandResult.Fail($"Texture file not found: {texPath}");

        texPath = Path.GetFullPath(texPath);

        int? meshIdx = null;
        if (args.Length >= 3)
        {
            if (!int.TryParse(args[2], out int mi))
                return CommandResult.Fail("Invalid mesh index.");
            if (mi < 0 || mi >= model.Meshes.Count)
                return CommandResult.Fail($"Mesh index {mi} out of range (0-{model.Meshes.Count - 1}).");
            meshIdx = mi;
        }

        int updated = 0;
        int start = meshIdx ?? 0;
        int end = meshIdx.HasValue ? meshIdx.Value + 1 : model.Meshes.Count;

        for (int i = start; i < end; i++)
        {
            var newMesh = _loader.Retexture(model.Meshes[i], texPath);
            if (newMesh is not null)
            {
                model.Meshes[i] = newMesh;
                updated++;
            }
        }

        if (updated == 0)
            return CommandResult.Fail("Failed to apply texture — check file format.");

        _referenceModelState.NotifyChanged();
        return CommandResult.Ok($"Retextured {updated} mesh(es) on model [{modelIdx}] with {Path.GetFileName(texPath)}");
    }
}

public sealed class RefTexEmissiveCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceModelLoader _loader;

    public string Name => "reftex-emissive";
    public string[] Aliases => ["refemissive"];
    public string HelpText => "Apply emissive texture to a reference model. Usage: reftex-emissive <modelIndex> <texturePath> [brightness] [meshIndex]\n" +
        "  brightness: emissive multiplier (default 1.0). Omit meshIndex for all meshes.";

    public RefTexEmissiveCommand(ReferenceModelState referenceModelState, ReferenceModelLoader loader)
    {
        _referenceModelState = referenceModelState;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2)
            return CommandResult.Fail(HelpText);

        if (!int.TryParse(args[0], out int modelIdx))
            return CommandResult.Fail("Invalid model index.");

        var model = _referenceModelState.Get(modelIdx);
        if (model is null)
            return CommandResult.Fail($"No model at index {modelIdx}.");

        string texPath = args[1];
        if (!File.Exists(texPath))
            return CommandResult.Fail($"Emissive texture not found: {texPath}");

        texPath = Path.GetFullPath(texPath);

        float brightness = 1f;
        if (args.Length >= 3 && !float.TryParse(args[2], out brightness))
            return CommandResult.Fail($"Invalid brightness: {args[2]}");

        int? meshIdx = null;
        if (args.Length >= 4)
        {
            if (!int.TryParse(args[3], out int mi))
                return CommandResult.Fail("Invalid mesh index.");
            if (mi < 0 || mi >= model.Meshes.Count)
                return CommandResult.Fail($"Mesh index {mi} out of range (0-{model.Meshes.Count - 1}).");
            meshIdx = mi;
        }

        int updated = 0;
        int start = meshIdx ?? 0;
        int end = meshIdx.HasValue ? meshIdx.Value + 1 : model.Meshes.Count;

        for (int i = start; i < end; i++)
        {
            var newMesh = _loader.RetextureEmissive(model.Meshes[i], texPath, brightness);
            if (newMesh is not null)
            {
                model.Meshes[i] = newMesh;
                updated++;
            }
        }

        if (updated == 0)
            return CommandResult.Fail("Failed to apply emissive texture — check file format.");

        _referenceModelState.NotifyChanged();
        return CommandResult.Ok($"Applied emissive to {updated} mesh(es) on model [{modelIdx}] (brightness={brightness:F1})");
    }
}

public sealed class RefSaveMetaCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;

    public string Name => "refsave";
    public string[] Aliases => [];
    public string HelpText => "Save ref model config to .refmeta file. Usage: refsave <modelIndex> <path>";

    public RefSaveMetaCommand(ReferenceModelState referenceModelState) => _referenceModelState = referenceModelState;

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 2)
            return CommandResult.Fail(HelpText);

        if (!int.TryParse(args[0], out int modelIdx))
            return CommandResult.Fail("Invalid model index.");

        var model = _referenceModelState.Get(modelIdx);
        if (model is null)
            return CommandResult.Fail($"No model at index {modelIdx}.");

        var path = args[1];
        if (!path.EndsWith(".refmeta", StringComparison.OrdinalIgnoreCase))
            path += ".refmeta";

        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path) ?? ".";

        var meta = ReferenceModelMeta.FromModel(model, dir);
        File.WriteAllText(path, meta.ToJson());

        return CommandResult.Ok($"Saved ref model [{modelIdx}] config to {path}");
    }
}

public sealed class RefLoadMetaCommand : IConsoleCommand
{
    private readonly ReferenceModelState _referenceModelState;
    private readonly ReferenceModelLoader _loader;

    public string Name => "refloadmeta";
    public string[] Aliases => ["refmeta"];
    public string HelpText => "Load ref model from .refmeta file. Usage: refloadmeta <path>";

    public RefLoadMetaCommand(ReferenceModelState referenceModelState, ReferenceModelLoader loader)
    {
        _referenceModelState = referenceModelState;
        _loader = loader;
    }

    public CommandResult Execute(string[] args, CommandContext context)
    {
        if (args.Length < 1)
            return CommandResult.Fail(HelpText);

        var path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
            return CommandResult.Fail($"File not found: {path}");

        string json;
        try { json = File.ReadAllText(path); }
        catch (Exception ex) { return CommandResult.Fail($"Failed to read file: {ex.Message}"); }

        var meta = ReferenceModelMeta.FromJson(json);
        if (meta is null)
            return CommandResult.Fail("Failed to parse .refmeta file.");

        var baseDir = Path.GetDirectoryName(path) ?? ".";
        meta.ResolvePaths(baseDir);

        // Load the model
        ReferenceModelData model;
        try
        {
            model = _loader.Load(meta.ModelPath);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail($"Failed to load model '{meta.ModelPath}': {ex.Message}");
        }

        // Apply transform
        model.PositionX = meta.PositionX;
        model.PositionY = meta.PositionY;
        model.PositionZ = meta.PositionZ;
        model.RotationX = meta.RotationX;
        model.RotationY = meta.RotationY;
        model.RotationZ = meta.RotationZ;
        model.Scale = meta.Scale;
        model.RenderMode = meta.RenderMode;
        model.IsVisible = meta.IsVisible;

        // Apply per-mesh texture overrides
        var warnings = new List<string>();
        if (meta.MeshOverrides is not null)
        {
            foreach (var ov in meta.MeshOverrides)
            {
                if (ov.MeshIndex < 0 || ov.MeshIndex >= model.Meshes.Count)
                {
                    warnings.Add($"Mesh index {ov.MeshIndex} out of range, skipped.");
                    continue;
                }

                if (ov.DiffuseTexturePath is not null)
                {
                    if (File.Exists(ov.DiffuseTexturePath))
                    {
                        var newMesh = _loader.Retexture(model.Meshes[ov.MeshIndex], ov.DiffuseTexturePath);
                        if (newMesh is not null)
                            model.Meshes[ov.MeshIndex] = newMesh;
                        else
                            warnings.Add($"Mesh {ov.MeshIndex}: failed to apply diffuse texture.");
                    }
                    else
                    {
                        warnings.Add($"Mesh {ov.MeshIndex}: diffuse texture not found: {ov.DiffuseTexturePath}");
                    }
                }

                if (ov.EmissiveTexturePath is not null)
                {
                    float brightness = ov.EmissiveBrightness ?? 1f;
                    if (File.Exists(ov.EmissiveTexturePath))
                    {
                        var newMesh = _loader.RetextureEmissive(
                            model.Meshes[ov.MeshIndex], ov.EmissiveTexturePath, brightness);
                        if (newMesh is not null)
                            model.Meshes[ov.MeshIndex] = newMesh;
                        else
                            warnings.Add($"Mesh {ov.MeshIndex}: failed to apply emissive texture.");
                    }
                    else
                    {
                        warnings.Add($"Mesh {ov.MeshIndex}: emissive texture not found: {ov.EmissiveTexturePath}");
                    }
                }
            }
        }

        // Apply animation state
        if (meta.Animation is not null && model.HasAnimations)
        {
            model.ActiveClipIndex = meta.Animation.ActiveClipIndex;
            model.AnimationSpeed = meta.Animation.Speed;
            if (meta.Animation.ActiveClipIndex.HasValue)
                model.IsAnimating = true;
        }

        _referenceModelState.Add(model);
        _referenceModelState.NotifyChanged();

        int idx = _referenceModelState.Models.Count - 1;
        string msg = $"Loaded [{idx}] from {Path.GetFileName(path)} — {model.Meshes.Count} meshes, {model.TotalVertices} vertices";
        if (warnings.Count > 0)
            msg += "\nWarnings:\n  " + string.Join("\n  ", warnings);

        return CommandResult.Ok(msg);
    }
}
