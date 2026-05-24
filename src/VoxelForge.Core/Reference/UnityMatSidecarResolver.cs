namespace VoxelForge.Core.Reference;

/// <summary>
/// Discovers Unity .mat sidecar files, matches them to model materials,
/// resolves texture GUIDs, and produces resolved material data for
/// reference model imports.
/// </summary>
public sealed class UnityMatSidecarResolver
{
    private readonly IReadOnlyList<string> _unityAssetRoots;

    /// <summary>
    /// Create a sidecar resolver that searches the given directories for .meta files.
    /// </summary>
    public UnityMatSidecarResolver(IReadOnlyList<string>? unityAssetRoots = null)
    {
        _unityAssetRoots = unityAssetRoots ?? [];
    }

    /// <summary>
    /// Discover and match Unity .mat sidecar files for the given model path,
    /// resolving texture GUIDs and producing material data.
    /// </summary>
    public UnityMatSidecarResult ProcessModel(
        string modelFilePath,
        IReadOnlyList<string> materialNames)
    {
        var result = new UnityMatSidecarResult();
        var modelDir = Path.GetDirectoryName(Path.GetFullPath(modelFilePath)) ?? ".";

        // 1. Discover .mat files near the model
        var candidateMatFiles = DiscoverMatFiles(modelDir);
        result.FoundAnyMatFiles = candidateMatFiles.Count > 0;

        if (candidateMatFiles.Count == 0)
        {
            result.GlobalWarnings.Add("No .mat sidecar files found near model directory.");
            // Create per-material no-match entries
            foreach (var materialName in materialNames)
            {
                result.Matches.Add(new UnityMatMatchResult
                {
                    MatFilePath = "(none)",
                    MatchedMaterialName = materialName,
                    ParsedData = null,
                    MatchKind = UnityMatMatchKind.None,
                    Warnings = { $"No .mat sidecar files found near model directory." },
                });
            }
            return result;
        }

        // 2. Parse all discovered .mat files
        var parsedMats = new List<(string path, UnityMatData data, string? name)>();
        foreach (var matPath in candidateMatFiles)
        {
            var data = UnityMatParser.ParseFile(matPath);
            if (data is not null)
            {
                parsedMats.Add((matPath, data, data.MaterialName));
            }
        }

        if (parsedMats.Count == 0)
        {
            result.GlobalWarnings.Add("Found .mat files but none could be parsed.");
            return result;
        }

        // 3. Build GUID map for texture resolution
        var roots = BuildSearchRoots(modelDir);
        var guidMap = MetaGuidResolver.BuildGuidMap(roots);
        result.GlobalWarnings.Add($"Scanned {guidMap.Count} texture entries from .meta files in {roots.Count} root(s).");

        // 4. Match parsed .mat files to material names
        foreach (var materialName in materialNames)
        {
            var matchData = MatchMatToMaterial(materialName, parsedMats);
            if (matchData is null)
            {
                result.Matches.Add(new UnityMatMatchResult
                {
                    MatFilePath = "(none)",
                    MatchedMaterialName = materialName,
                    ParsedData = null,
                    MatchKind = UnityMatMatchKind.None,
                    Warnings = { $"No matching .mat sidecar for material '{materialName}'." },
                });
                continue;
            }

            var (matPath, mat, kind, warnings) = matchData.Value;

            // 5. Resolve textures
            ResolveTextures(mat, guidMap, modelDir);
            mat.SourceFilePath = matPath;

            result.Matches.Add(new UnityMatMatchResult
            {
                MatFilePath = matPath,
                MatchedMaterialName = materialName,
                ParsedData = mat,
                MatchKind = kind,
                Warnings = warnings,
            });

            // Collect unresolved GUIDs
            foreach (var unresolvedGuid in mat.UnresolvedGuids)
            {
                result.GlobalWarnings.Add($"Unresolved texture GUID '{unresolvedGuid}' for material '{materialName}'.");
            }
        }

        return result;
    }

    private static List<string> DiscoverMatFiles(string modelDir)
    {
        var matFiles = new List<string>();

        try
        {
            if (Directory.Exists(modelDir))
            {
                var files = Directory.EnumerateFiles(
                    modelDir, "*.mat", SearchOption.TopDirectoryOnly);
                matFiles.AddRange(files);
            }
        }
        catch
        {
            // Ignore inaccessible directories
        }

        // Also check parent directory (common Unity project structure)
        var parentDir = Path.GetDirectoryName(modelDir);
        if (parentDir is not null && Directory.Exists(parentDir))
        {
            try
            {
                var parentFiles = Directory.EnumerateFiles(
                    parentDir, "*.mat", SearchOption.TopDirectoryOnly);
                foreach (var f in parentFiles)
                {
                    if (!matFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                        matFiles.Add(f);
                }
            }
            catch
            {
                // Ignore
            }
        }

        return matFiles;
    }

    private static List<string> BuildSearchRoots(string modelDir)
    {
        var roots = new List<string>();
        roots.Add(modelDir);

        // Add parent directory
        var parent = Path.GetDirectoryName(modelDir);
        if (parent is not null && !parent.Equals(modelDir, StringComparison.Ordinal))
        {
            roots.Add(parent);
        }

        // Walk up looking for Assets/ (Unity project root)
        var dir = modelDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets")))
            {
                roots.Add(Path.Combine(dir, "Assets"));
                break;
            }
            if (Directory.Exists(Path.Combine(dir, "Library")))
            {
                roots.Add(Path.Combine(dir, "Assets"));
                break;
            }
            var parent2 = Path.GetDirectoryName(dir);
            if (parent2 is null || parent2 == dir)
                break;
            dir = parent2;
        }

        return roots;
    }

    private static (string path, UnityMatData data, UnityMatMatchKind kind, List<string> warnings)? MatchMatToMaterial(
        string materialName,
        List<(string path, UnityMatData data, string? name)> parsedMats)
    {
        if (parsedMats.Count == 0)
            return null;

        // Strategy 1: Exact name match between m_Name in .mat and material name
        var exactMatches = parsedMats
            .Where(m => string.Equals(m.name, materialName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (exactMatches.Count == 1)
        {
            var m = exactMatches[0];
            return (m.path, m.data, UnityMatMatchKind.ExactName, []);
        }

        if (exactMatches.Count > 1)
        {
            // Ambiguous — pick first, warn
            var m = exactMatches[0];
            var others = exactMatches.Skip(1).Select(x => Path.GetFileName(x.path));
            return (m.path, m.data, UnityMatMatchKind.Ambiguous,
                [$"Multiple .mat files match material '{materialName}' by name. Using '{Path.GetFileName(m.path)}' (also: {string.Join(", ", others)})."]);
        }

        // Strategy 2: Filename stem match
        var filenameCandidates = parsedMats
            .Where(m => string.Equals(
                Path.GetFileNameWithoutExtension(m.path),
                materialName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filenameCandidates.Count == 1)
        {
            var m = filenameCandidates[0];
            return (m.path, m.data, UnityMatMatchKind.FilenameStem,
                [$"Matched '{Path.GetFileName(m.path)}' to material '{materialName}' by filename stem."]);
        }

        if (filenameCandidates.Count > 1)
        {
            var m = filenameCandidates[0];
            var others = filenameCandidates.Skip(1).Select(x => Path.GetFileName(x.path));
            return (m.path, m.data, UnityMatMatchKind.Ambiguous,
                [$"Multiple .mat files match material '{materialName}' by filename. Using '{Path.GetFileName(m.path)}' (also: {string.Join(", ", others)})."]);
        }

        // No match found
        return null;
    }

    private static void ResolveTextures(
        UnityMatData data,
        Dictionary<string, string> guidMap,
        string modelDir)
    {
        // Resolve each texture reference
        if (data.MainTex is not null)
        {
            var resolved = ResolveTextureRef(data.MainTex, guidMap, modelDir);
            if (resolved is not null)
            {
                if (resolved.StartsWith("(unresolved:"))
                    data.UnresolvedGuids.Add(data.MainTex.Guid!);
                else
                    data.ResolvedTextures["_MainTex"] = resolved;
            }
        }

        if (data.BaseColorMap is not null)
        {
            var resolved = ResolveTextureRef(data.BaseColorMap, guidMap, modelDir);
            if (resolved is not null)
            {
                if (resolved.StartsWith("(unresolved:"))
                    data.UnresolvedGuids.Add(data.BaseColorMap.Guid!);
                else
                    data.ResolvedTextures["_BaseColorMap"] = resolved;
            }
        }

        if (data.EmissionMap is not null)
        {
            var resolved = ResolveTextureRef(data.EmissionMap, guidMap, modelDir);
            if (resolved is not null)
            {
                if (resolved.StartsWith("(unresolved:"))
                    data.UnresolvedGuids.Add(data.EmissionMap.Guid!);
                else
                    data.ResolvedTextures["_EmissionMap"] = resolved;
            }
        }
    }

    private static string? ResolveTextureRef(
        UnityTextureRef texRef,
        Dictionary<string, string> guidMap,
        string modelDir)
    {
        // Try GUID resolution first
        if (texRef.Guid is not null)
        {
            if (guidMap.TryGetValue(texRef.Guid, out var resolvedPath))
                return resolvedPath;

            // Track this as unresolved
            return $"(unresolved:{texRef.Guid})";
        }

        // Path-based reference — try relative to model dir
        if (texRef.PathHint is not null)
        {
            var candidate = Path.Combine(modelDir, texRef.PathHint);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            var filename = Path.GetFileName(texRef.PathHint);
            candidate = Path.Combine(modelDir, filename);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }
}
