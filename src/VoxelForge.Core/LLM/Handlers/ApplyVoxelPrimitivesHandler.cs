using System.Text;
using System.Text.Json;
using VoxelForge.Core.Services;

namespace VoxelForge.Core.LLM.Handlers;

public sealed class ApplyVoxelPrimitivesHandler : IToolHandler
{
    private readonly VoxelPrimitiveGenerationService _generationService;

    public ApplyVoxelPrimitivesHandler(VoxelPrimitiveGenerationService generationService)
    {
        _generationService = generationService;
    }

    public string ToolName => "apply_voxel_primitives";

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Generate voxel assignments from compact block, box, and line primitives.",
        ParametersSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "primitives": {
                    "type": "array",
                    "minItems": 1,
                    "items": {
                        "type": "object",
                        "properties": {
                            "id": {
                                "type": "string",
                                "description": "Optional caller label for diagnostics only."
                            },
                            "kind": {
                                "type": "string",
                                "enum": ["block", "box", "line"]
                            },
                            "palette_index": {
                                "type": "integer",
                                "minimum": 1,
                                "maximum": 255
                            },
                            "at": {
                                "type": "object",
                                "properties": {
                                    "x": { "type": "integer" },
                                    "y": { "type": "integer" },
                                    "z": { "type": "integer" }
                                },
                                "required": ["x", "y", "z"]
                            },
                            "from": {
                                "type": "object",
                                "properties": {
                                    "x": { "type": "integer" },
                                    "y": { "type": "integer" },
                                    "z": { "type": "integer" }
                                },
                                "required": ["x", "y", "z"]
                            },
                            "to": {
                                "type": "object",
                                "properties": {
                                    "x": { "type": "integer" },
                                    "y": { "type": "integer" },
                                    "z": { "type": "integer" }
                                },
                                "required": ["x", "y", "z"]
                            },
                            "mode": {
                                "type": "string",
                                "enum": ["filled", "shell", "edges"],
                                "description": "Box fill mode. Defaults to filled. Ignored for block and line."
                            },
                            "radius": {
                                "type": "integer",
                                "minimum": 0,
                                "maximum": 16,
                                "description": "Line brush radius in Chebyshev voxel distance. Defaults to 0."
                            }
                        },
                        "required": ["kind", "palette_index"]
                    }
                },
                "max_generated_voxels": {
                    "type": "integer",
                    "minimum": 1,
                    "maximum": 65536,
                    "description": "Safety cap after de-duplication. Defaults to 8192."
                },
                "preview_only": {
                    "type": "boolean",
                    "description": "If true, validate and report generated counts without mutating. Defaults to false."
                }
            },
            "required": ["primitives"]
        }
        """).RootElement,
    };

    public ToolHandlerResult Handle(JsonElement arguments, VoxelModel model, LabelIndex labels, List<AnimationClip> clips)
    {
        ParseResult parseResult = TryParseRequest(arguments);
        if (!parseResult.Success)
        {
            return new ToolHandlerResult
            {
                Content = parseResult.Message,
                IsError = true,
            };
        }

        VoxelPrimitiveGenerationResult result = _generationService.BuildIntent(parseResult.Request!);
        return new ToolHandlerResult
        {
            Content = FormatResult(result),
            IsError = !result.Success,
            MutationIntent = result.Intent,
        };
    }

    private static ParseResult TryParseRequest(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return ParseResult.Failure("apply_voxel_primitives arguments must be an object.");

        if (!arguments.TryGetProperty("primitives", out JsonElement primitivesElement))
            return ParseResult.Failure("apply_voxel_primitives requires primitives.");

        if (primitivesElement.ValueKind != JsonValueKind.Array)
            return ParseResult.Failure("apply_voxel_primitives primitives must be an array.");

        var primitives = new List<VoxelPrimitiveRequest>();
        int primitiveIndex = 0;
        foreach (JsonElement primitiveElement in primitivesElement.EnumerateArray())
        {
            PrimitiveParseResult primitiveResult = TryParsePrimitive(primitiveElement, primitiveIndex);
            if (!primitiveResult.Success)
                return ParseResult.Failure(primitiveResult.Message);

            primitives.Add(primitiveResult.Primitive!);
            primitiveIndex++;
        }

        int maxGeneratedVoxels = VoxelPrimitiveGenerationService.DefaultMaxGeneratedVoxels;
        if (arguments.TryGetProperty("max_generated_voxels", out JsonElement maxGeneratedVoxelsElement))
        {
            if (!TryReadInt(maxGeneratedVoxelsElement, out maxGeneratedVoxels))
                return ParseResult.Failure("apply_voxel_primitives max_generated_voxels must be an integer.");
        }

        bool previewOnly = false;
        if (arguments.TryGetProperty("preview_only", out JsonElement previewOnlyElement))
        {
            if (previewOnlyElement.ValueKind != JsonValueKind.True && previewOnlyElement.ValueKind != JsonValueKind.False)
                return ParseResult.Failure("apply_voxel_primitives preview_only must be a boolean.");

            previewOnly = previewOnlyElement.GetBoolean();
        }

        return ParseResult.Ok(new ApplyVoxelPrimitivesRequest
        {
            Primitives = primitives,
            MaxGeneratedVoxels = maxGeneratedVoxels,
            PreviewOnly = previewOnly,
        });
    }

    private static PrimitiveParseResult TryParsePrimitive(JsonElement primitiveElement, int primitiveIndex)
    {
        if (primitiveElement.ValueKind != JsonValueKind.Object)
            return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} must be an object.");

        if (!TryReadRequiredString(primitiveElement, "kind", out string? kindText))
            return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} requires string kind.");

        if (!TryParseKind(kindText, out VoxelPrimitiveKind kind))
            return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} has unsupported kind '{kindText}'.");

        if (!TryReadRequiredInt(primitiveElement, "palette_index", out int paletteIndex))
            return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} requires integer palette_index.");

        string? id = null;
        if (primitiveElement.TryGetProperty("id", out JsonElement idElement))
        {
            if (idElement.ValueKind != JsonValueKind.String)
                return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} id must be a string.");

            id = idElement.GetString();
        }

        VoxelPrimitivePoint? at = null;
        if (primitiveElement.TryGetProperty("at", out JsonElement atElement))
        {
            PointParseResult pointResult = TryParsePoint(atElement, primitiveIndex, "at");
            if (!pointResult.Success)
                return PrimitiveParseResult.Failure(pointResult.Message);

            at = pointResult.Point;
        }

        VoxelPrimitivePoint? from = null;
        if (primitiveElement.TryGetProperty("from", out JsonElement fromElement))
        {
            PointParseResult pointResult = TryParsePoint(fromElement, primitiveIndex, "from");
            if (!pointResult.Success)
                return PrimitiveParseResult.Failure(pointResult.Message);

            from = pointResult.Point;
        }

        VoxelPrimitivePoint? to = null;
        if (primitiveElement.TryGetProperty("to", out JsonElement toElement))
        {
            PointParseResult pointResult = TryParsePoint(toElement, primitiveIndex, "to");
            if (!pointResult.Success)
                return PrimitiveParseResult.Failure(pointResult.Message);

            to = pointResult.Point;
        }

        VoxelBoxMode mode = VoxelBoxMode.Filled;
        if (primitiveElement.TryGetProperty("mode", out JsonElement modeElement))
        {
            if (modeElement.ValueKind != JsonValueKind.String)
                return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} mode must be a string.");

            string? modeText = modeElement.GetString();
            if (!TryParseMode(modeText, out mode))
                return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} has unsupported box mode '{modeText}'.");
        }

        int radius = 0;
        if (primitiveElement.TryGetProperty("radius", out JsonElement radiusElement))
        {
            if (!TryReadInt(radiusElement, out radius))
                return PrimitiveParseResult.Failure($"Primitive {primitiveIndex} radius must be an integer.");
        }

        return PrimitiveParseResult.Ok(new VoxelPrimitiveRequest
        {
            Id = id,
            Kind = kind,
            PaletteIndex = paletteIndex,
            At = at,
            From = from,
            To = to,
            Mode = mode,
            Radius = radius,
        });
    }

    private static PointParseResult TryParsePoint(JsonElement pointElement, int primitiveIndex, string propertyName)
    {
        if (pointElement.ValueKind != JsonValueKind.Object)
            return PointParseResult.Failure($"Primitive {primitiveIndex} {propertyName} must be an object.");

        if (!TryReadRequiredInt(pointElement, "x", out int x))
            return PointParseResult.Failure($"Primitive {primitiveIndex} {propertyName}.x must be an integer.");

        if (!TryReadRequiredInt(pointElement, "y", out int y))
            return PointParseResult.Failure($"Primitive {primitiveIndex} {propertyName}.y must be an integer.");

        if (!TryReadRequiredInt(pointElement, "z", out int z))
            return PointParseResult.Failure($"Primitive {primitiveIndex} {propertyName}.z must be an integer.");

        return PointParseResult.Ok(new VoxelPrimitivePoint(x, y, z));
    }

    private static bool TryReadRequiredString(JsonElement parent, string propertyName, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
            return false;

        if (element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString();
        return value is not null;
    }

    private static bool TryReadRequiredInt(JsonElement parent, string propertyName, out int value)
    {
        value = 0;
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
            return false;

        return TryReadInt(element, out value);
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
            return false;

        return element.TryGetInt32(out value);
    }

    private static bool TryParseKind(string? text, out VoxelPrimitiveKind kind)
    {
        kind = VoxelPrimitiveKind.Block;
        if (string.Equals(text, "block", StringComparison.Ordinal))
        {
            kind = VoxelPrimitiveKind.Block;
            return true;
        }

        if (string.Equals(text, "box", StringComparison.Ordinal))
        {
            kind = VoxelPrimitiveKind.Box;
            return true;
        }

        if (string.Equals(text, "line", StringComparison.Ordinal))
        {
            kind = VoxelPrimitiveKind.Line;
            return true;
        }

        return false;
    }

    private static bool TryParseMode(string? text, out VoxelBoxMode mode)
    {
        mode = VoxelBoxMode.Filled;
        if (string.Equals(text, "filled", StringComparison.Ordinal))
        {
            mode = VoxelBoxMode.Filled;
            return true;
        }

        if (string.Equals(text, "shell", StringComparison.Ordinal))
        {
            mode = VoxelBoxMode.Shell;
            return true;
        }

        if (string.Equals(text, "edges", StringComparison.Ordinal))
        {
            mode = VoxelBoxMode.Edges;
            return true;
        }

        return false;
    }

    private static string FormatResult(VoxelPrimitiveGenerationResult result)
    {
        var builder = new StringBuilder();
        builder.Append(result.Message);

        for (int i = 0; i < result.Summaries.Count; i++)
        {
            VoxelPrimitiveSummary summary = result.Summaries[i];
            builder.AppendLine();
            builder.Append("Primitive ");
            builder.Append(i);
            if (!string.IsNullOrEmpty(summary.Id))
            {
                builder.Append(" (");
                builder.Append(summary.Id);
                builder.Append(')');
            }

            builder.Append(": ");
            builder.Append(summary.Kind);
            builder.Append(" palette ");
            builder.Append(summary.PaletteIndex);
            builder.Append(", generated ");
            builder.Append(summary.GeneratedVoxelCount);
            builder.Append(", final unique ");
            builder.Append(summary.UniqueVoxelCountAfterBatchMerge);

            if (summary.Bounds is not null)
            {
                builder.Append(", bounds ");
                AppendPoint(builder, summary.Bounds.Min);
                builder.Append("..");
                AppendPoint(builder, summary.Bounds.Max);
            }
        }

        return builder.ToString();
    }

    private static void AppendPoint(StringBuilder builder, Point3 point)
    {
        builder.Append('(');
        builder.Append(point.X);
        builder.Append(',');
        builder.Append(point.Y);
        builder.Append(',');
        builder.Append(point.Z);
        builder.Append(')');
    }

    private sealed class ParseResult
    {
        private ParseResult(bool success, string message, ApplyVoxelPrimitivesRequest? request)
        {
            Success = success;
            Message = message;
            Request = request;
        }

        public bool Success { get; }
        public string Message { get; }
        public ApplyVoxelPrimitivesRequest? Request { get; }

        public static ParseResult Ok(ApplyVoxelPrimitivesRequest request)
        {
            return new ParseResult(true, string.Empty, request);
        }

        public static ParseResult Failure(string message)
        {
            return new ParseResult(false, message, null);
        }
    }

    private sealed class PrimitiveParseResult
    {
        private PrimitiveParseResult(bool success, string message, VoxelPrimitiveRequest? primitive)
        {
            Success = success;
            Message = message;
            Primitive = primitive;
        }

        public bool Success { get; }
        public string Message { get; }
        public VoxelPrimitiveRequest? Primitive { get; }

        public static PrimitiveParseResult Ok(VoxelPrimitiveRequest primitive)
        {
            return new PrimitiveParseResult(true, string.Empty, primitive);
        }

        public static PrimitiveParseResult Failure(string message)
        {
            return new PrimitiveParseResult(false, message, null);
        }
    }

    private sealed class PointParseResult
    {
        private PointParseResult(bool success, string message, VoxelPrimitivePoint point)
        {
            Success = success;
            Message = message;
            Point = point;
        }

        public bool Success { get; }
        public string Message { get; }
        public VoxelPrimitivePoint Point { get; }

        public static PointParseResult Ok(VoxelPrimitivePoint point)
        {
            return new PointParseResult(true, string.Empty, point);
        }

        public static PointParseResult Failure(string message)
        {
            return new PointParseResult(false, message, default);
        }
    }
}
