using System.Text.Json;

namespace VoxelForge.Import;

public sealed class ImportPlanValidator
{
    private static readonly string[] MutatingTools = [
        "set_voxels",
        "remove_voxels",
        "apply_voxel_primitives",
        "set_palette_entry",
        "set_grid_hint",
        "create_region",
        "assign_voxels_to_region",
        "delete_region",
        "fill_box",
        "clear_model",
    ];

    private static readonly string[] LifecycleTools = [
        "new_model",
    ];

    private static readonly string[] ReadOnlyTools = [
        "describe_model",
        "get_model_info",
        "get_voxel",
        "count_voxels",
        "get_voxels_in_area",
        "list_regions",
        "get_region_voxels",
        "get_region_bounds",
        "get_region_tree",
        "get_region_neighbors",
        "get_interface_voxels",
        "measure_distance",
        "get_cross_section",
        "check_collision",
    ];

    private static readonly string[] SupportedCommands = [
        "set",
        "remove",
        "fill",
        "clear",
        "palette",
        "regions",
        "label",
        "grid",
        "undo",
        "redo",
    ];

    private static readonly string[] FilesystemCommands = [
        "save",
        "load",
    ];

    public IReadOnlyList<ImportDiagnostic> ValidateOperations(
        IReadOnlyList<ImportPlanOperation> operations,
        ImportNormalizeOptions options,
        IReadOnlyList<ImportDiagnostic> existingDiagnostics,
        string sourcePath)
    {
        var diagnostics = new List<ImportDiagnostic>(existingDiagnostics);

        if (options.MaxOperations < 1)
        {
            diagnostics.Add(CreateError(
                "IMPORT022",
                "max_operations must be at least 1.",
                sourcePath,
                jsonPointer: "/options/max_operations"));
        }

        if (options.MaxGeneratedVoxels < 1)
        {
            diagnostics.Add(CreateError(
                "IMPORT023",
                "max_generated_voxels must be at least 1.",
                sourcePath,
                jsonPointer: "/options/max_generated_voxels"));
        }

        if (operations.Count > options.MaxOperations)
        {
            diagnostics.Add(CreateError(
                "IMPORT021",
                $"Operation count {operations.Count} exceeds max_operations {options.MaxOperations}.",
                sourcePath,
                operationIndex: operations.Count,
                jsonPointer: "/operations"));
        }

        for (int i = 0; i < operations.Count; i++)
        {
            ImportPlanOperation operation = operations[i];
            if (operation.Kind == "tool_call")
                ValidateToolCall(diagnostics, operation, sourcePath, options.MaxGeneratedVoxels);
            else if (operation.Kind == "console_command")
                ValidateConsoleCommand(diagnostics, operation, sourcePath);
            else
                diagnostics.Add(CreateError("IMPORT020", $"Unsupported operation kind '{operation.Kind}'.", sourcePath, operation));
        }

        return diagnostics;
    }

    public string ResolveToolEffect(string toolName)
    {
        if (Contains(LifecycleTools, toolName))
            return "lifecycle";
        if (Contains(MutatingTools, toolName))
            return "mutation";
        if (Contains(ReadOnlyTools, toolName))
            return "read_only";
        return "unsupported";
    }

    public string ResolveCommandEffect(string commandName)
    {
        if (Contains(SupportedCommands, commandName))
            return "mutation";
        return "unsupported";
    }

    private static void ValidateToolCall(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        int maxGeneratedVoxels)
    {
        string effect = operation.Effect;
        if (effect == "unsupported")
        {
            diagnostics.Add(CreateError(
                "IMPORT101",
                $"Unsupported tool '{operation.Name}'.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/name"));
            return;
        }

        if (effect == "read_only")
        {
            diagnostics.Add(CreateWarning(
                "IMPORT201",
                $"Read-only tool '{operation.Name}' will be recorded but skipped during replay.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/name"));
        }
        else
        {
            ValidateToolArguments(diagnostics, operation, sourcePath);
        }

        ValidateResourceCaps(diagnostics, operation, sourcePath, maxGeneratedVoxels);
    }

    private static void ValidateConsoleCommand(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath)
    {
        if (Contains(FilesystemCommands, operation.Name))
        {
            diagnostics.Add(CreateError(
                "IMPORT102",
                $"Unsupported filesystem command '{operation.Name}' in transcript import; use import CLI paths instead.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/request/command"));
            return;
        }

        if (!Contains(SupportedCommands, operation.Name))
        {
            diagnostics.Add(CreateError(
                "IMPORT101",
                $"Unsupported console command '{operation.Name}'.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/request/command"));
            return;
        }

        if (operation.Name == "undo" || operation.Name == "redo")
        {
            diagnostics.Add(CreateWarning(
                "IMPORT202",
                $"Console command '{operation.Name}' depends on exact prior replay history.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/request/command"));
        }

        ValidateConsoleCommandArguments(diagnostics, operation, sourcePath);
    }

    private static void ValidateConsoleCommandArguments(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath)
    {
        if (operation.Arguments.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateError("IMPORT110", "Console command request must be an object.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/request"));
            return;
        }

        if (operation.Arguments.TryGetProperty("args", out JsonElement argsElement) && argsElement.ValueKind != JsonValueKind.Null && argsElement.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(CreateError("IMPORT110", "request.args must be an array when provided.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/request/args"));
            return;
        }

        if (!operation.Arguments.TryGetProperty("args", out argsElement) || argsElement.ValueKind == JsonValueKind.Null)
            argsElement = JsonSerializer.SerializeToElement(Array.Empty<string>());

        if (!ValidateArgsAreStrings(diagnostics, operation, sourcePath, argsElement))
            return;

        if (operation.Name == "set")
        {
            ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 4);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 0);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 1);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 2);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 3);
            return;
        }

        if (operation.Name == "remove")
        {
            ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 3);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 0);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 1);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 2);
            return;
        }

        if (operation.Name == "fill")
        {
            ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 7);
            for (int i = 0; i < 6; i++)
                ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, i);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 6);
            return;
        }

        if (operation.Name == "grid")
        {
            if (argsElement.GetArrayLength() > 0)
            {
                ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 1);
                ValidateConsolePositiveIntArg(diagnostics, operation, sourcePath, argsElement, 0);
            }
            return;
        }

        if (operation.Name == "palette")
        {
            if (argsElement.GetArrayLength() == 0)
                return;

            if (!TryGetConsoleArg(argsElement, 0, out string? subcommand) || subcommand != "add")
            {
                diagnostics.Add(CreateError("IMPORT111", "palette command only supports 'palette add' during replay.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/request/args/0"));
                return;
            }

            ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 6);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 1);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 3);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 4);
            ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 5);
            if (argsElement.GetArrayLength() >= 7)
                ValidateConsoleByteArg(diagnostics, operation, sourcePath, argsElement, 6);
            return;
        }

        if (operation.Name == "label")
        {
            ValidateConsoleArgCount(diagnostics, operation, sourcePath, argsElement, 4);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 1);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 2);
            ValidateConsoleIntArg(diagnostics, operation, sourcePath, argsElement, 3);
        }
    }

    private static bool ValidateArgsAreStrings(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement argsElement)
    {
        int index = 0;
        foreach (JsonElement arg in argsElement.EnumerateArray())
        {
            if (arg.ValueKind != JsonValueKind.String)
            {
                diagnostics.Add(CreateError("IMPORT110", $"request.args[{index}] must be a string.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/request/args/{index}"));
                return false;
            }

            index++;
        }

        return true;
    }

    private static void ValidateConsoleArgCount(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement argsElement,
        int requiredCount)
    {
        if (argsElement.GetArrayLength() < requiredCount)
            diagnostics.Add(CreateError("IMPORT111", $"Console command '{operation.Name}' requires at least {requiredCount} argument(s).", sourcePath, operation, toolName: operation.Name, jsonPointer: "/request/args"));
    }

    private static void ValidateConsoleIntArg(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement argsElement,
        int index)
    {
        if (!TryGetConsoleArg(argsElement, index, out string? text))
            return;

        if (!int.TryParse(text, out int _))
            diagnostics.Add(CreateError("IMPORT111", $"request.args[{index}] must be an integer.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/request/args/{index}"));
    }

    private static void ValidateConsolePositiveIntArg(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement argsElement,
        int index)
    {
        if (!TryGetConsoleArg(argsElement, index, out string? text))
            return;

        if (!int.TryParse(text, out int value) || value < 1)
            diagnostics.Add(CreateError("IMPORT112", $"request.args[{index}] must be an integer at least 1.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/request/args/{index}"));
    }

    private static void ValidateConsoleByteArg(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement argsElement,
        int index)
    {
        if (!TryGetConsoleArg(argsElement, index, out string? text))
            return;

        if (!byte.TryParse(text, out byte _))
            diagnostics.Add(CreateError("IMPORT112", $"request.args[{index}] must be an integer from 0 to 255.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/request/args/{index}"));
    }

    private static bool TryGetConsoleArg(JsonElement argsElement, int index, out string? text)
    {
        text = null;
        if (argsElement.ValueKind != JsonValueKind.Array || index < 0 || index >= argsElement.GetArrayLength())
            return false;

        JsonElement element = argsElement[index];
        if (element.ValueKind != JsonValueKind.String)
            return false;

        text = element.GetString();
        return true;
    }

    private static void ValidateToolArguments(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath)
    {
        if (operation.Arguments.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateError(
                "IMPORT110",
                $"Arguments for '{operation.Name}' must be a JSON object.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/arguments"));
            return;
        }

        if (operation.Name == "set_voxels")
        {
            ValidatePointArray(diagnostics, operation, sourcePath, "voxels", requirePaletteIndex: true);
            return;
        }

        if (operation.Name == "remove_voxels")
        {
            ValidatePointArray(diagnostics, operation, sourcePath, "positions", requirePaletteIndex: false);
            return;
        }

        if (operation.Name == "new_model")
        {
            ValidateRequiredString(diagnostics, operation, sourcePath, "name");
            ValidateOptionalIntRange(diagnostics, operation, sourcePath, "grid_hint", 1, 256);
            ValidateInitialPaletteEntries(diagnostics, operation, sourcePath);
            return;
        }

        if (operation.Name == "set_palette_entry")
        {
            ValidatePaletteIndex(diagnostics, operation, sourcePath, "/arguments/index", "index");
            ValidateRequiredString(diagnostics, operation, sourcePath, "name");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "r");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "g");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "b");
            ValidateOptionalByte(diagnostics, operation, sourcePath, "a");
            return;
        }

        if (operation.Name == "set_grid_hint")
        {
            ValidateRequiredIntRange(diagnostics, operation, sourcePath, "size", 1, 256);
            return;
        }

        if (operation.Name == "create_region")
        {
            ValidateRequiredString(diagnostics, operation, sourcePath, "name");
            ValidateOptionalString(diagnostics, operation, sourcePath, "parent_id");
            ValidateStringDictionary(diagnostics, operation, sourcePath, "properties");
            return;
        }

        if (operation.Name == "delete_region")
        {
            ValidateRequiredString(diagnostics, operation, sourcePath, "region_id");
            return;
        }

        if (operation.Name == "assign_voxels_to_region")
        {
            ValidateAssignVoxelsToRegion(diagnostics, operation, sourcePath);
            return;
        }

        if (operation.Name == "fill_box")
        {
            ValidateRequiredInt(diagnostics, operation, sourcePath, "x1");
            ValidateRequiredInt(diagnostics, operation, sourcePath, "y1");
            ValidateRequiredInt(diagnostics, operation, sourcePath, "z1");
            ValidateRequiredInt(diagnostics, operation, sourcePath, "x2");
            ValidateRequiredInt(diagnostics, operation, sourcePath, "y2");
            ValidateRequiredInt(diagnostics, operation, sourcePath, "z2");
            ValidatePaletteIndex(diagnostics, operation, sourcePath, "/arguments/palette_index", "palette_index");
            return;
        }

        if (operation.Name == "clear_model")
            return;

        if (operation.Name == "apply_voxel_primitives")
        {
            if (!operation.Arguments.TryGetProperty("primitives", out JsonElement primitives) || primitives.ValueKind != JsonValueKind.Array)
                diagnostics.Add(CreateError("IMPORT111", "apply_voxel_primitives requires primitives array.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/primitives"));
        }
    }

    private static void ValidateInitialPaletteEntries(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath)
    {
        if (!operation.Arguments.TryGetProperty("palette_entries", out JsonElement entries) || entries.ValueKind == JsonValueKind.Null)
            return;

        if (entries.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(CreateError("IMPORT110", "palette_entries must be an array.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/palette_entries"));
            return;
        }

        int index = 0;
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateError("IMPORT110", $"palette_entries[{index}] must be an object.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/arguments/palette_entries/{index}"));
                index++;
                continue;
            }

            ValidatePaletteIndex(diagnostics, operation, sourcePath, $"/arguments/palette_entries/{index}/index", "index", entry);
            ValidateRequiredString(diagnostics, operation, sourcePath, "name", entry, $"/arguments/palette_entries/{index}/name");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "r", entry, $"/arguments/palette_entries/{index}/r");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "g", entry, $"/arguments/palette_entries/{index}/g");
            ValidateRequiredByte(diagnostics, operation, sourcePath, "b", entry, $"/arguments/palette_entries/{index}/b");
            ValidateOptionalByte(diagnostics, operation, sourcePath, "a", entry, $"/arguments/palette_entries/{index}/a");
            index++;
        }
    }

    private static void ValidateAssignVoxelsToRegion(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath)
    {
        ValidateRequiredString(diagnostics, operation, sourcePath, "region_id");
        bool hasPositions = operation.Arguments.TryGetProperty("positions", out JsonElement positions) && positions.ValueKind != JsonValueKind.Null;
        bool hasBox = operation.Arguments.TryGetProperty("box", out JsonElement box) && box.ValueKind != JsonValueKind.Null;
        if (hasPositions == hasBox)
        {
            diagnostics.Add(CreateError("IMPORT111", "assign_voxels_to_region requires exactly one of positions or box.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments"));
            return;
        }

        if (hasPositions)
        {
            ValidatePointArray(diagnostics, operation, sourcePath, "positions", requirePaletteIndex: false);
            return;
        }

        if (box.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateError("IMPORT110", "box must be an object.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/box"));
            return;
        }

        ValidateRequiredInt(diagnostics, operation, sourcePath, "x1", box, "/arguments/box/x1");
        ValidateRequiredInt(diagnostics, operation, sourcePath, "y1", box, "/arguments/box/y1");
        ValidateRequiredInt(diagnostics, operation, sourcePath, "z1", box, "/arguments/box/z1");
        ValidateRequiredInt(diagnostics, operation, sourcePath, "x2", box, "/arguments/box/x2");
        ValidateRequiredInt(diagnostics, operation, sourcePath, "y2", box, "/arguments/box/y2");
        ValidateRequiredInt(diagnostics, operation, sourcePath, "z2", box, "/arguments/box/z2");
    }

    private static void ValidateRequiredString(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        ValidateRequiredString(diagnostics, operation, sourcePath, propertyName, operation.Arguments, "/arguments/" + propertyName);
    }

    private static void ValidateRequiredString(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        JsonElement element,
        string jsonPointer)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires non-empty string {propertyName}.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
    }

    private static void ValidateOptionalString(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        if (!operation.Arguments.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
            return;

        if (property.ValueKind != JsonValueKind.String)
            diagnostics.Add(CreateError("IMPORT110", $"{operation.Name} property {propertyName} must be a string.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
    }

    private static void ValidateStringDictionary(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        if (!operation.Arguments.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
            return;

        if (property.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(CreateError("IMPORT110", $"{propertyName} must be an object.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
            return;
        }

        foreach (JsonProperty entry in property.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
                diagnostics.Add(CreateError("IMPORT110", $"{propertyName}.{entry.Name} must be a string.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName + "/" + entry.Name));
        }
    }

    private static void ValidateRequiredInt(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        ValidateRequiredInt(diagnostics, operation, sourcePath, propertyName, operation.Arguments, "/arguments/" + propertyName);
    }

    private static void ValidateRequiredInt(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        JsonElement element,
        string jsonPointer)
    {
        if (!TryGetRequiredInt(element, propertyName, out int _))
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires integer {propertyName}.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
    }

    private static void ValidateRequiredIntRange(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        int min,
        int max)
    {
        if (!TryGetRequiredInt(operation.Arguments, propertyName, out int value))
        {
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires integer {propertyName}.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
            return;
        }

        if (value < min || value > max)
            diagnostics.Add(CreateError("IMPORT112", $"{propertyName} must be between {min} and {max}.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
    }

    private static void ValidateOptionalIntRange(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        int min,
        int max)
    {
        if (!operation.Arguments.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
            return;

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
        {
            diagnostics.Add(CreateError("IMPORT111", $"{propertyName} must be an integer.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
            return;
        }

        if (value < min || value > max)
            diagnostics.Add(CreateError("IMPORT112", $"{propertyName} must be between {min} and {max}.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
    }

    private static void ValidateRequiredByte(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        ValidateRequiredByte(diagnostics, operation, sourcePath, propertyName, operation.Arguments, "/arguments/" + propertyName);
    }

    private static void ValidateRequiredByte(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        JsonElement element,
        string jsonPointer)
    {
        if (!TryGetRequiredInt(element, propertyName, out int value))
        {
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires integer {propertyName}.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
            return;
        }

        if (value < 0 || value > 255)
            diagnostics.Add(CreateError("IMPORT112", $"{propertyName} must be between 0 and 255.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
    }

    private static void ValidateOptionalByte(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName)
    {
        ValidateOptionalByte(diagnostics, operation, sourcePath, propertyName, operation.Arguments, "/arguments/" + propertyName);
    }

    private static void ValidateOptionalByte(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        JsonElement element,
        string jsonPointer)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
            return;

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out int value))
        {
            diagnostics.Add(CreateError("IMPORT111", $"{propertyName} must be an integer.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
            return;
        }

        if (value < 0 || value > 255)
            diagnostics.Add(CreateError("IMPORT112", $"{propertyName} must be between 0 and 255.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
    }

    private static void ValidatePointArray(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string propertyName,
        bool requirePaletteIndex)
    {
        if (!operation.Arguments.TryGetProperty(propertyName, out JsonElement points) || points.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires {propertyName} array.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/" + propertyName));
            return;
        }

        int index = 0;
        foreach (JsonElement point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(CreateError("IMPORT110", $"{propertyName}[{index}] must be an object.", sourcePath, operation, toolName: operation.Name, jsonPointer: $"/arguments/{propertyName}/{index}"));
                index++;
                continue;
            }

            ValidatePointCoordinate(diagnostics, operation, sourcePath, point, propertyName, index, "x");
            ValidatePointCoordinate(diagnostics, operation, sourcePath, point, propertyName, index, "y");
            ValidatePointCoordinate(diagnostics, operation, sourcePath, point, propertyName, index, "z");
            if (requirePaletteIndex)
                ValidatePaletteIndex(diagnostics, operation, sourcePath, $"/arguments/{propertyName}/{index}/i", "i", point);

            index++;
        }
    }

    private static void ValidatePointCoordinate(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        JsonElement point,
        string propertyName,
        int index,
        string coordinateName)
    {
        if (!TryGetRequiredInt(point, coordinateName, out int _))
        {
            diagnostics.Add(CreateError(
                "IMPORT111",
                $"{propertyName}[{index}] requires integer {coordinateName}.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: $"/arguments/{propertyName}/{index}/{coordinateName}"));
        }
    }

    private static void ValidatePaletteIndex(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string jsonPointer,
        string propertyName)
    {
        ValidatePaletteIndex(diagnostics, operation, sourcePath, jsonPointer, propertyName, operation.Arguments);
    }

    private static void ValidatePaletteIndex(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        string jsonPointer,
        string propertyName,
        JsonElement element)
    {
        if (!TryGetRequiredInt(element, propertyName, out int paletteIndex))
        {
            diagnostics.Add(CreateError("IMPORT111", $"{operation.Name} requires integer {propertyName}.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
            return;
        }

        if (paletteIndex < 1 || paletteIndex > 255)
        {
            diagnostics.Add(CreateError("IMPORT112", $"Palette index must be between 1 and 255.", sourcePath, operation, toolName: operation.Name, jsonPointer: jsonPointer));
        }
    }

    private static bool TryGetRequiredInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }

    private static void ValidateResourceCaps(
        List<ImportDiagnostic> diagnostics,
        ImportPlanOperation operation,
        string sourcePath,
        int maxGeneratedVoxels)
    {
        if (operation.Arguments.ValueKind != JsonValueKind.Object)
            return;

        if (operation.Arguments.TryGetProperty("voxels", out JsonElement voxels)
            && voxels.ValueKind == JsonValueKind.Array
            && voxels.GetArrayLength() > maxGeneratedVoxels)
        {
            diagnostics.Add(CreateError(
                "IMPORT103",
                $"Voxel array length {voxels.GetArrayLength()} exceeds max_generated_voxels {maxGeneratedVoxels}.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/arguments/voxels"));
        }

        if (operation.Arguments.TryGetProperty("positions", out JsonElement positions)
            && positions.ValueKind == JsonValueKind.Array
            && positions.GetArrayLength() > maxGeneratedVoxels)
        {
            diagnostics.Add(CreateError(
                "IMPORT103",
                $"Position array length {positions.GetArrayLength()} exceeds max_generated_voxels {maxGeneratedVoxels}.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/arguments/positions"));
        }

        if (operation.Arguments.TryGetProperty("max_generated_voxels", out JsonElement maxElement)
            && maxElement.ValueKind == JsonValueKind.Number
            && maxElement.TryGetInt32(out int requestedMax)
            && requestedMax > maxGeneratedVoxels)
        {
            diagnostics.Add(CreateError(
                "IMPORT104",
                $"Requested max_generated_voxels {requestedMax} exceeds cap {maxGeneratedVoxels}.",
                sourcePath,
                operation,
                toolName: operation.Name,
                jsonPointer: "/arguments/max_generated_voxels"));
        }
    }

    private static bool Contains(string[] values, string value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ImportDiagnostic CreateError(
        string code,
        string message,
        string sourcePath,
        ImportPlanOperation? operation = null,
        int? operationIndex = null,
        string? toolName = null,
        string? jsonPointer = null)
    {
        return CreateDiagnostic(ImportDiagnosticSeverity.Error, code, message, sourcePath, operation, operationIndex, toolName, jsonPointer);
    }

    private static ImportDiagnostic CreateWarning(
        string code,
        string message,
        string sourcePath,
        ImportPlanOperation operation,
        string? toolName,
        string? jsonPointer)
    {
        return CreateDiagnostic(ImportDiagnosticSeverity.Warning, code, message, sourcePath, operation, null, toolName, jsonPointer);
    }

    private static ImportDiagnostic CreateDiagnostic(
        ImportDiagnosticSeverity severity,
        string code,
        string message,
        string sourcePath,
        ImportPlanOperation? operation,
        int? operationIndex,
        string? toolName,
        string? jsonPointer)
    {
        return new ImportDiagnostic
        {
            Severity = severity,
            Code = code,
            Message = message,
            SourcePath = sourcePath,
            Line = operation?.SourceLine,
            OperationIndex = operationIndex ?? operation?.SourceIndex,
            ToolName = toolName,
            SourceCallId = operation?.SourceCallId,
            JsonPointer = jsonPointer,
        };
    }
}
