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

        if (operation.Name == "set_palette_entry")
        {
            ValidatePaletteIndex(diagnostics, operation, sourcePath, "/arguments/index", "index");
            return;
        }

        if (operation.Name == "set_grid_hint")
        {
            if (!TryGetRequiredInt(operation.Arguments, "size", out int size))
            {
                diagnostics.Add(CreateError("IMPORT111", "set_grid_hint requires integer size.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/size"));
                return;
            }

            if (size < 1)
                diagnostics.Add(CreateError("IMPORT112", "set_grid_hint size must be at least 1.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/size"));
            return;
        }

        if (operation.Name == "apply_voxel_primitives")
        {
            if (!operation.Arguments.TryGetProperty("primitives", out JsonElement primitives) || primitives.ValueKind != JsonValueKind.Array)
                diagnostics.Add(CreateError("IMPORT111", "apply_voxel_primitives requires primitives array.", sourcePath, operation, toolName: operation.Name, jsonPointer: "/arguments/primitives"));
        }
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
