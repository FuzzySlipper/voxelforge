using System.Globalization;

namespace VoxelForge.Core.Reference;

/// <summary>
/// Resolves Unity GUID references to actual file paths by scanning .meta files.
/// Supports searching under given root directories for matching guid entries.
/// </summary>
public static class MetaGuidResolver
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".tiff", ".tif", ".exr", ".hdr", ".psd", ".dds",
    };

    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".js", ".shader", ".shadergraph", ".unity", ".prefab", ".asset",
        ".anim", ".controller", ".mat", ".fbx", ".obj", ".blend", ".mb", ".ma",
        ".asmdef", ".rsp", ".meta", ".dll", ".lib", ".a",
    };

    /// <summary>
    /// Scan all .meta files under the given root directories and build a
    /// dictionary mapping GUID → resolved file path for image/texture assets.
    /// </summary>
    public static Dictionary<string, string> BuildGuidMap(IEnumerable<string> rootDirs)
    {
        var guidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootDir in rootDirs)
        {
            if (!Directory.Exists(rootDir))
                continue;

            try
            {
                var metaFiles = Directory.EnumerateFiles(
                    rootDir, "*.meta", SearchOption.AllDirectories);

                foreach (var metaPath in metaFiles)
                {
                    var assetPath = Path.ChangeExtension(metaPath, null);
                    var ext = Path.GetExtension(assetPath);

                    // Skip non-image extensions
                    if (string.IsNullOrEmpty(ext) || IgnoredExtensions.Contains(ext))
                        continue;

                    // Only interested in image/texture files
                    if (!ImageExtensions.Contains(ext) && !ShouldConsiderUnknownExt(ext))
                        continue;

                    try
                    {
                        var guid = ExtractGuid(metaPath);
                        if (guid is not null && !guidMap.ContainsKey(guid))
                        {
                            guidMap[guid] = Path.GetFullPath(assetPath);
                        }
                    }
                    catch
                    {
                        // Skip unreadable .meta files
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return guidMap;
    }

    /// <summary>
    /// Extract the guid value from a Unity .meta file.
    /// Format: "guid: abcdef1234567890abcdef1234567890"
    /// </summary>
    public static string? ExtractGuid(string metaFilePath)
    {
        if (!File.Exists(metaFilePath))
            return null;

        // Read just the first ~20 lines; guid is usually early in the file
        using var reader = new StreamReader(metaFilePath);
        for (int i = 0; i < 30; i++)
        {
            if (reader.EndOfStream)
                break;

            var line = reader.ReadLine();
            if (line is null)
                break;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed["guid:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve a GUID to a file path by scanning .meta files under candidate roots.
    /// Returns null if not found.
    /// </summary>
    public static string? ResolveGuid(string guid, IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                var metaFiles = Directory.EnumerateFiles(
                    root, "*.meta", SearchOption.AllDirectories);

                foreach (var metaPath in metaFiles)
                {
                    try
                    {
                        var foundGuid = ExtractGuid(metaPath);
                        if (string.Equals(foundGuid, guid, StringComparison.OrdinalIgnoreCase))
                        {
                            var assetPath = Path.ChangeExtension(metaPath, null);
                            var ext = Path.GetExtension(assetPath);
                            if (!string.IsNullOrEmpty(ext) && !IgnoredExtensions.Contains(ext))
                            {
                                return Path.GetFullPath(assetPath);
                            }
                        }
                    }
                    catch
                    {
                        // Skip unreadable files
                    }
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        return null;
    }

    private static bool ShouldConsiderUnknownExt(string ext)
    {
        // Consider any extension that isn't explicitly ignored
        return !IgnoredExtensions.Contains(ext);
    }
}
