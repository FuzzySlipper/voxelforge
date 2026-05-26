using System.Globalization;
using System.Text.RegularExpressions;

namespace VoxelForge.Core.Reference;

/// <summary>
/// Lightweight line-based parser for Unity .mat YAML sidecar files.
/// No external YAML dependency — handles the specific Unity material format.
/// </summary>
public static partial class UnityMatParser
{
    // Regex to extract {fileID: N, guid: "abcdef...", type: N} blocks
    [GeneratedRegex(@"\{fileID:\s*(\d+),\s*guid:\s*([a-fA-F0-9]+),\s*type:\s*(\d+)\}")]
    private static partial Regex FileIdGuidRegex();

    // Regex to extract {r: N, g: N, b: N, a: N} color blocks
    [GeneratedRegex(@"\{r:\s*([\d.eE+-]+),\s*g:\s*([\d.eE+-]+),\s*b:\s*([\d.eE+-]+),\s*a:\s*([\d.eE+-]+)\}")
]
    private static partial Regex ColorBlockRegex();

    /// <summary>
    /// Parse a Unity .mat file at the given path. Returns null if the file
    /// does not exist or cannot be parsed as a Unity Material.
    /// </summary>
    public static UnityMatData? ParseFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        string content;
        try
        {
            content = File.ReadAllText(filePath);
        }
        catch
        {
            return null;
        }

        return Parse(content, filePath);
    }

    /// <summary>
    /// Parse Unity .mat YAML content string.
    /// </summary>
    public static UnityMatData? Parse(string yamlContent, string? sourcePath = null)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
            return null;

        var lines = yamlContent.Split('\n', StringSplitOptions.None);
        var data = new UnityMatData
        {
            SourceFilePath = sourcePath ?? string.Empty,
        };

        // State machine for parsing
        bool inMaterialBlock = false;
        bool inSavedProperties = false;
        bool inTexEnvs = false;
        bool inFloats = false;
        bool inColors = false;
        string? currentTexEnvKey = null;
        int texEnvIndent = 0;
        int savedPropertiesIndent = 0;

        var ignoredProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            // Skip empty lines and YAML directives
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('%'))
                continue;

            // Detect Material: block start (at top-level indent)
            if (trimmed == "Material:" && indent < 2)
            {
                inMaterialBlock = true;
                continue;
            }

            if (!inMaterialBlock)
                continue;

            // Detect m_Name
            if (trimmed.StartsWith("m_Name:"))
            {
                data.MaterialName = ExtractQuotedValue(trimmed);
                continue;
            }

            // Detect m_SavedProperties
            if (trimmed.StartsWith("m_SavedProperties:"))
            {
                inSavedProperties = true;
                savedPropertiesIndent = indent;
                continue;
            }

            if (inSavedProperties)
            {
                // Check if we left saved properties (dedent back to or above saved properties level,
                // but not continuing a section)
                if (indent <= savedPropertiesIndent && !trimmed.StartsWith('-') &&
                    !trimmed.StartsWith("m_TexEnvs:") && !trimmed.StartsWith("m_Floats:") &&
                    !trimmed.StartsWith("m_Colors:"))
                {
                    inSavedProperties = false;
                    inTexEnvs = false;
                    inFloats = false;
                    inColors = false;
                    currentTexEnvKey = null;
                    continue;
                }

                // Detect list items
                if (trimmed.StartsWith("- ") && trimmed.Length > 2)
                {
                    var sectionContent = trimmed[2..];

                    // Check if this is a tex env entry like "- _MainTex:"
                    if (inTexEnvs || sectionContent.Contains(':'))
                    {
                        var key = ExtractKeyBeforeColon(sectionContent);
                        if (key is not null)
                        {
                            // If we were in a previous tex env entry, end it
                            currentTexEnvKey = null;

                            // Check if this starts a new tex env entry
                            if (!inFloats && !inColors)
                            {
                                inTexEnvs = true;
                                currentTexEnvKey = key;
                                texEnvIndent = indent;
                                continue;
                            }
                        }
                    }

                    // Float entries: "- _Cutoff: 0.5"
                    if (inFloats && sectionContent.Contains(':'))
                    {
                        ParseFloatEntry(sectionContent, data, ignoredProps);
                        continue;
                    }

                    // Color entries: "- _Color: {r: 1, g: 1, b: 1, a: 1}"
                    if (inColors && sectionContent.Contains(':'))
                    {
                        ParseColorEntry(sectionContent, data, ignoredProps);
                        continue;
                    }
                }

                // Detect section headers: "m_TexEnvs:", "m_Floats:", "m_Colors:"
                if (trimmed == "m_TexEnvs:")
                {
                    inTexEnvs = false;
                    inFloats = false;
                    inColors = false;
                    currentTexEnvKey = null;
                    continue;
                }
                if (trimmed == "m_Floats:")
                {
                    inTexEnvs = false;
                    inFloats = true;
                    inColors = false;
                    currentTexEnvKey = null;
                    continue;
                }
                if (trimmed == "m_Colors:")
                {
                    inTexEnvs = false;
                    inFloats = false;
                    inColors = true;
                    currentTexEnvKey = null;
                    continue;
                }

                // If in tex envs section, handle multi-line entries
                if (inTexEnvs && currentTexEnvKey is not null)
                {
                    // m_Texture: {fileID: ..., guid: ..., type: ...}
                    if (trimmed.StartsWith("m_Texture:"))
                    {
                        var texRef = ParseTextureRef(trimmed);
                        if (texRef is not null)
                        {
                            AssignTextureRef(currentTexEnvKey, texRef, data);
                        }
                        continue;
                    }

                    // m_Scale: {x: 1, y: 1} - skip for now
                    if (trimmed.StartsWith("m_Scale:") || trimmed.StartsWith("m_Offset:"))
                    {
                        continue;
                    }

                    // Check if we left the tex env entry (dedented past tex env indent)
                    // and this line isn't a continuation
                    if (indent <= texEnvIndent && !trimmed.StartsWith('-') &&
                        !trimmed.StartsWith("m_TexEnvs:") && !trimmed.StartsWith("m_Floats:") &&
                        !trimmed.StartsWith("m_Colors:"))
                    {
                        currentTexEnvKey = null;
                        inTexEnvs = false;
                    }
                }
            }
        }

        // Track ignored properties (ones we found but didn't use)
        foreach (var ignored in ignoredProps)
        {
            if (!IsKnownProperty(ignored))
                data.IgnoredProperties.Add(ignored);
        }

        return data;
    }

    private static string? ExtractKeyBeforeColon(string sectionContent)
    {
        var colonIdx = sectionContent.IndexOf(':');
        return colonIdx > 0 ? sectionContent[..colonIdx].Trim() : null;
    }

    private static UnityTextureRef? ParseTextureRef(string line)
    {
        // Try GUID-based {fileID: N, guid: "...", type: N} first
        var guidMatch = FileIdGuidRegex().Match(line);
        if (guidMatch.Success)
        {
            long fileId = long.Parse(guidMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            string guid = guidMatch.Groups[2].Value.ToLowerInvariant();
            int type = int.Parse(guidMatch.Groups[3].Value, CultureInfo.InvariantCulture);

            return new UnityTextureRef
            {
                FileId = fileId,
                Guid = guid,
                Type = type,
            };
        }

        // Fall back to direct path-like reference (e.g. m_Texture: Textures/diffuse.png)
        // or quoted variants (e.g. m_Texture: "Textures/diffuse.png")
        var colonIdx = line.IndexOf(':');
        if (colonIdx >= 0)
        {
            var valuePart = line[(colonIdx + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(valuePart) && !valuePart.StartsWith('{'))
            {
                // Strip surrounding quotes if present
                var pathValue = valuePart;
                if ((pathValue.StartsWith('"') && pathValue.EndsWith('"')) ||
                    (pathValue.StartsWith('\'') && pathValue.EndsWith('\'')))
                {
                    pathValue = pathValue[1..^1].Trim();
                }

                if (!string.IsNullOrWhiteSpace(pathValue))
                {
                    return new UnityTextureRef
                    {
                        PathHint = pathValue,
                    };
                }
            }
        }

        return null;
    }

    private static void AssignTextureRef(string propertyName, UnityTextureRef texRef, UnityMatData data)
    {
        var lower = propertyName.ToLowerInvariant();
        if (lower is "_maintex" or "_basemap")
        {
            data.MainTex = texRef;
        }
        else if (lower is "_basecolormap" or "_base_color_map")
        {
            data.BaseColorMap = texRef;
        }
        else if (lower is "_emissionmap" or "_emission_map" or "_emissive")
        {
            data.EmissionMap = texRef;
        }
        else
        {
            // Unknown texture property — track as ignored
            if (!data.IgnoredProperties.Contains(propertyName))
                data.IgnoredProperties.Add(propertyName);
        }
    }

    private static void ParseFloatEntry(string sectionContent, UnityMatData data, HashSet<string> ignoredProps)
    {
        // Format: "_PropertyName: value"
        var colonIdx = sectionContent.IndexOf(':');
        if (colonIdx < 0)
            return;

        var propName = sectionContent[..colonIdx].Trim();
        var valueStr = sectionContent[(colonIdx + 1)..].Trim();

        if (!float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            ignoredProps.Add(propName);
            return;
        }

        var lower = propName.ToLowerInvariant();
        switch (lower)
        {
            case "_cutoff":
                data.Cutoff = value;
                break;
            case "_glossiness":
                data.Glossiness = value;
                break;
            case "_metallic":
                data.Metallic = value;
                break;
            case "_smoothness":
                data.Glossiness = value;
                break;
            default:
                ignoredProps.Add(propName);
                break;
        }
    }

    private static void ParseColorEntry(string sectionContent, UnityMatData data, HashSet<string> ignoredProps)
    {
        // Format: "_PropertyName: {r: N, g: N, b: N, a: N}"
        var colonIdx = sectionContent.IndexOf(':');
        if (colonIdx < 0)
            return;

        var propName = sectionContent[..colonIdx].Trim();
        var valueStr = sectionContent[(colonIdx + 1)..].Trim();

        var match = ColorBlockRegex().Match(valueStr);
        if (!match.Success)
        {
            ignoredProps.Add(propName);
            return;
        }

        float r = float.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        float g = float.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        float b = float.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        float a = float.Parse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture);

        var color = new UnityVector4(r, g, b, a);

        var lower = propName.ToLowerInvariant();
        switch (lower)
        {
            case "_color":
            case "_basecolor":
            case "_base_color":
                data.MainColor = color;
                break;
            case "_emissioncolor":
            case "_emission_color":
                data.EmissionColor = color;
                break;
            default:
                ignoredProps.Add(propName);
                break;
        }
    }

    private static string? ExtractQuotedValue(string line)
    {
        var colonIdx = line.IndexOf(':');
        if (colonIdx < 0)
            return null;

        var valuePart = line[(colonIdx + 1)..].Trim();
        if (valuePart.StartsWith('"') && valuePart.EndsWith('"'))
            return valuePart[1..^1];

        return valuePart;
    }

    private static bool IsKnownProperty(string propName)
    {
        var lower = propName.ToLowerInvariant();
        return knownProperties.Contains(lower);
    }

    private static readonly HashSet<string> knownProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "_maintex", "_basemap", "_basecolormap", "_base_color_map",
        "_emissionmap", "_emission_map", "_emissive",
        "_color", "_basecolor", "_base_color",
        "_emissioncolor", "_emission_color",
        "_cutoff", "_glossiness", "_metallic", "_smoothness",
    };
}
