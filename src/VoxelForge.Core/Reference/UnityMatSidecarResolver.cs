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
            ResolveTextures(mat, guidMap, modelDir, matPath);
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

    private List<string> DiscoverMatFiles(string modelDir)
    {
        var matFiles = new List<string>();

        // 1. Check model directory (top-level only)
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

        // 2. Check parent directory (common Unity project structure)
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

        // 3. Walk up from modelDir to discover nearby Unity Assets roots
        //    and scan them recursively for .mat files (with safe caps).
        //    Works even when no explicit constructor roots are provided,
        //    so the default new UnityMatSidecarResolver() finds practical sidecars.
        const int maxMatFiles = 1000;
        const int maxFilesScanned = 5000;

        var discoveryRoots = new List<string>();

        // Walk up looking for Assets/ or Library/ (Unity project root)
        var dir = modelDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, "Assets")))
            {
                discoveryRoots.Add(Path.Combine(dir, "Assets"));
                break;
            }
            if (Directory.Exists(Path.Combine(dir, "Library")))
            {
                discoveryRoots.Add(Path.Combine(dir, "Assets"));
                break;
            }
            var parent = Path.GetDirectoryName(dir);
            if (parent is null || parent == dir)
                break;
            dir = parent;
        }

        // Also include explicit constructor roots
        foreach (var root in _unityAssetRoots)
        {
            if (!string.IsNullOrWhiteSpace(root) &&
                !discoveryRoots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                discoveryRoots.Add(root);
            }
        }

        // Deduplicate and sort for deterministic ordering
        discoveryRoots.Sort(StringComparer.OrdinalIgnoreCase);

        // Scan each discovery root recursively
        foreach (var root in discoveryRoots)
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                int filesScanned = 0;
                foreach (var f in Directory.EnumerateFiles(
                    root, "*.mat", SearchOption.AllDirectories))
                {
                    if (filesScanned >= maxFilesScanned)
                        break;
                    filesScanned++;

                    if (!matFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                    {
                        matFiles.Add(f);
                        if (matFiles.Count >= maxMatFiles)
                            break;
                    }
                }
            }
            catch
            {
                // Ignore inaccessible subdirectories
            }

            if (matFiles.Count >= maxMatFiles)
                break;
        }

        // Deterministic ordering
        matFiles.Sort(StringComparer.OrdinalIgnoreCase);

        return matFiles;
    }

    private List<string> BuildSearchRoots(string modelDir)
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

        // Add configured asset roots for .meta GUID resolution
        foreach (var root in _unityAssetRoots)
        {
            if (!string.IsNullOrWhiteSpace(root) &&
                Directory.Exists(root) &&
                !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(root);
            }
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

        // Strategy 3: Substring/prefix match — handles cases where material name
        // (e.g. "GOLEM_NORMAL_ROCK") contains or is contained by .mat m_Name (e.g. "Golem").
        // This is a documented alias convention: if the .mat name is a prefix or significant
        // substring of the material name, it's considered a match. Asset-level sidecars
        // (.vf-reference-settings.json) can provide explicit alias maps for ambiguous cases.
        var substringCandidates = parsedMats
            .Where(m =>
            {
                var matName = m.name ?? Path.GetFileNameWithoutExtension(m.path);
                if (string.IsNullOrWhiteSpace(matName)) return false;
                return materialName.StartsWith(matName, StringComparison.OrdinalIgnoreCase) ||
                       matName.StartsWith(materialName, StringComparison.OrdinalIgnoreCase) ||
                       materialName.Contains(matName, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (substringCandidates.Count == 1)
        {
            var m = substringCandidates[0];
            return (m.path, m.data, UnityMatMatchKind.FilenameStem,
                [$"Matched '{Path.GetFileName(m.path)}' (m_Name='{m.name ?? "(unknown)"}') to material '{materialName}' by substring/prefix match."]);
        }

        if (substringCandidates.Count > 1)
        {
            var m = substringCandidates[0];
            var others = substringCandidates.Skip(1).Select(x => Path.GetFileName(x.path));
            return (m.path, m.data, UnityMatMatchKind.Ambiguous,
                [$"Multiple .mat files match material '{materialName}' by substring. Using '{Path.GetFileName(m.path)}' (also: {string.Join(", ", others)})."]);
        }

        // No match found
        return null;
    }

    private static void ResolveTextures(
        UnityMatData data,
        Dictionary<string, string> guidMap,
        string modelDir,
        string? matFilePath = null)
    {
        string? matDir = matFilePath is not null
            ? Path.GetDirectoryName(Path.GetFullPath(matFilePath))
            : null;

        // Resolve each texture reference
        if (data.MainTex is not null)
        {
            var resolved = ResolveTextureRef(data.MainTex, guidMap, modelDir, matDir);
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
            var resolved = ResolveTextureRef(data.BaseColorMap, guidMap, modelDir, matDir);
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
            var resolved = ResolveTextureRef(data.EmissionMap, guidMap, modelDir, matDir);
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
        string modelDir,
        string? matDir = null)
    {
        // Try GUID resolution first
        if (texRef.Guid is not null)
        {
            if (guidMap.TryGetValue(texRef.Guid, out var resolvedPath))
                return resolvedPath;

            // Track this as unresolved
            return $"(unresolved:{texRef.Guid})";
        }

        // Path-based reference — try relative to .mat file directory first,
        // then relative to model directory
        if (texRef.PathHint is not null)
        {
            string filename;

            // 1. Try relative to .mat file directory
            if (matDir is not null)
            {
                var candidate = Path.Combine(matDir, texRef.PathHint);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                filename = Path.GetFileName(texRef.PathHint);
                candidate = Path.Combine(matDir, filename);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            // 2. Try relative to model directory
            var candidate2 = Path.Combine(modelDir, texRef.PathHint);
            if (File.Exists(candidate2))
                return Path.GetFullPath(candidate2);

            filename = Path.GetFileName(texRef.PathHint);
            candidate2 = Path.Combine(modelDir, filename);
            if (File.Exists(candidate2))
                return Path.GetFullPath(candidate2);
        }

        return null;
    }
}
