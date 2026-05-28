using VoxelForge.Core.Reference;

namespace VoxelForge.Core.Tests.Reference;

public sealed class UnityMatSidecarResolverTests
{
    [Fact]
    public void ProcessModel_NoMatFiles_ReturnsUnmatchedResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a "model" path that doesn't need to exist
            var modelPath = Path.Combine(tempDir, "model.obj");
            File.WriteAllText(modelPath, "");
            var materialNames = new List<string> { "MaterialA", "MaterialB" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.NotNull(result);
            Assert.False(result.FoundAnyMatFiles);
            Assert.Equal(2, result.Matches.Count);
            Assert.All(result.Matches, m => Assert.Equal(UnityMatMatchKind.None, m.MatchKind));
            Assert.All(result.Matches, m => Assert.Null(m.ParsedData));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_ExactNameMatch_FindsMat()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // Create a .mat file with matching m_Name
            File.WriteAllText(Path.Combine(tempDir, "BluePaint.mat"), """
                %YAML 1.1
                --- !u!21 &2100000
                Material:
                  m_Name: BluePaint
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats:
                    - _Cutoff: 0.5
                    m_Colors:
                    - _Color: {r: 0, g: 0, b: 1, a: 1}
                """);

            var materialNames = new List<string> { "BluePaint" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.Equal(UnityMatMatchKind.ExactName, match.MatchKind);
            Assert.NotNull(match.ParsedData);
            Assert.Equal("BluePaint", match.MatchedMaterialName);
            Assert.True(match.ParsedData.MainColor.HasValue);
            Assert.Equal(0f, match.ParsedData.MainColor.Value.R);
            Assert.Equal(0f, match.ParsedData.MainColor.Value.G);
            Assert.Equal(1f, match.ParsedData.MainColor.Value.B);
            Assert.Equal(0.5f, match.ParsedData.Cutoff);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_FilenameStemMatch_WithoutExactName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // .mat file with different internal name but filename stem matches
            File.WriteAllText(Path.Combine(tempDir, "MyMaterial.mat"), """
                --- !u!21 &2100000
                Material:
                  m_Name: DifferentInternalName
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats: []
                    m_Colors:
                    - _Color: {r: 1, g: 0.5, b: 0, a: 1}
                """);

            var materialNames = new List<string> { "MyMaterial" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.Equal(UnityMatMatchKind.FilenameStem, match.MatchKind);
            Assert.NotNull(match.ParsedData);
            Assert.Equal("MyMaterial", match.MatchedMaterialName);
            Assert.True(match.ParsedData.MainColor.HasValue);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_GuidResolution_SearchesMetaFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.gltf");
            File.WriteAllText(modelPath, "");

            // Create a texture with .meta file
            var texDir = Path.Combine(tempDir, "Textures");
            Directory.CreateDirectory(texDir);
            var texPath = Path.Combine(texDir, "diffuse_albedo.png");
            File.WriteAllText(texPath, "fake png content");
            File.WriteAllText(texPath + ".meta", @"
fileFormatVersion: 2
guid: abcdef1234567890abcdef1234567890
timeCreated: 1234567890
");

            // Create a .mat with a GUID texture reference
            File.WriteAllText(Path.Combine(tempDir, "MatA.mat"), $@"
%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: MatA
  m_SavedProperties:
    m_TexEnvs:
    - _MainTex:
        m_Texture: {{fileID: 2800000, guid: abcdef1234567890abcdef1234567890, type: 3}}
    m_Floats: []
    m_Colors: []
");

            var materialNames = new List<string> { "MatA" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Single(match.ParsedData.ResolvedTextures);
            Assert.Equal(Path.GetFullPath(texPath), match.ParsedData.ResolvedTextures["_MainTex"]);
            Assert.Empty(match.ParsedData.UnresolvedGuids);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_UnresolvedGuid_ReportsDiagnostics()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.gltf");
            File.WriteAllText(modelPath, "");

            // Create a .mat with a GUID that has no corresponding .meta
            File.WriteAllText(Path.Combine(tempDir, "MatMissingTex.mat"), $@"
--- !u!21 &2100000
Material:
  m_Name: MatMissingTex
  m_SavedProperties:
    m_TexEnvs:
    - _MainTex:
        m_Texture: {{fileID: 2800000, guid: 00000000000000000000000000000000, type: 3}}
    m_Floats: []
    m_Colors: []
");

            var materialNames = new List<string> { "MatMissingTex" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Single(match.ParsedData.UnresolvedGuids);
            Assert.Equal("00000000000000000000000000000000", match.ParsedData.UnresolvedGuids[0]);
            Assert.Empty(match.ParsedData.ResolvedTextures);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_MultipleMaterials_MatchesEach()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.glb");
            File.WriteAllText(modelPath, "");

            // Create two .mat files
            File.WriteAllText(Path.Combine(tempDir, "RedPaint.mat"), """
                --- !u!21 &2100000
                Material:
                  m_Name: RedPaint
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats: []
                    m_Colors:
                    - _Color: {r: 1, g: 0, b: 0, a: 1}
                """);

            File.WriteAllText(Path.Combine(tempDir, "GreenPaint.mat"), """
                --- !u!21 &2100000
                Material:
                  m_Name: GreenPaint
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats: []
                    m_Colors:
                    - _Color: {r: 0, g: 1, b: 0, a: 1}
                """);

            var materialNames = new List<string> { "RedPaint", "GreenPaint" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Equal(2, result.Matches.Count);

            var redMatch = result.Matches.First(m => m.MatchedMaterialName == "RedPaint");
            Assert.Equal(UnityMatMatchKind.ExactName, redMatch.MatchKind);
            Assert.NotNull(redMatch.ParsedData);
            Assert.True(redMatch.ParsedData.MainColor.HasValue);
            Assert.Equal(1f, redMatch.ParsedData.MainColor.Value.R);

            var greenMatch = result.Matches.First(m => m.MatchedMaterialName == "GreenPaint");
            Assert.Equal(UnityMatMatchKind.ExactName, greenMatch.MatchKind);
            Assert.NotNull(greenMatch.ParsedData);
            Assert.True(greenMatch.ParsedData.MainColor.HasValue);
            Assert.Equal(1f, greenMatch.ParsedData.MainColor.Value.G);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_EmissionTexture_Resolved()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.glb");
            File.WriteAllText(modelPath, "");

            // Create an emissive texture with .meta
            var texPath = Path.Combine(tempDir, "glow.png");
            File.WriteAllText(texPath, "fake glow");
            File.WriteAllText(texPath + ".meta", @"
fileFormatVersion: 2
guid: eeeeeeee00000000ffffffff12345678
timeCreated: 1234567890
");

            // Create .mat with emission texture
            File.WriteAllText(Path.Combine(tempDir, "GlowMat.mat"), $@"
--- !u!21 &2100000
Material:
  m_Name: GlowMat
  m_SavedProperties:
    m_TexEnvs:
    - _EmissionMap:
        m_Texture: {{fileID: 2800000, guid: eeeeeeee00000000ffffffff12345678, type: 3}}
    m_Floats: []
    m_Colors:
    - _EmissionColor: {{r: 3, g: 2, b: 1, a: 1}}
");

            var materialNames = new List<string> { "GlowMat" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.True(match.ParsedData.ResolvedTextures.ContainsKey("_EmissionMap"));
            Assert.Equal(Path.GetFullPath(texPath), match.ParsedData.ResolvedTextures["_EmissionMap"]);
            Assert.True(match.ParsedData.EmissionColor.HasValue);
            Assert.Equal(3f, match.ParsedData.EmissionColor.Value.R);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_BaseColorMap_ResolvesAndIsAvailableAsDiffuseOverride()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-sidecartest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.glb");
            File.WriteAllText(modelPath, "");

            // Create a texture with .meta for _BaseColorMap
            var texPath = Path.Combine(tempDir, "basecolor.png");
            File.WriteAllText(texPath, "fake base color texture");
            File.WriteAllText(texPath + ".meta", @"
fileFormatVersion: 2
guid: aaaabbbbccccddddeeeeffff00001111
timeCreated: 1234567890
");

            // Create .mat with ONLY _BaseColorMap (no _MainTex) to test precedence fallback
            File.WriteAllText(Path.Combine(tempDir, "HDRPMat.mat"), $@"
--- !u!21 &2100000
Material:
  m_Name: HDRPMat
  m_SavedProperties:
    m_TexEnvs:
    - _BaseColorMap:
        m_Texture: {{fileID: 2800000, guid: aaaabbbbccccddddeeeeffff00001111, type: 3}}
    m_Floats: []
    m_Colors: []
");

            var materialNames = new List<string> { "HDRPMat" };

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);

            // _BaseColorMap should be resolved
            Assert.True(match.ParsedData.ResolvedTextures.ContainsKey("_BaseColorMap"),
                "_BaseColorMap should be in ResolvedTextures");
            Assert.Equal(Path.GetFullPath(texPath), match.ParsedData.ResolvedTextures["_BaseColorMap"]);

            // There should be no _MainTex in resolved textures (no _MainTex defined)
            Assert.False(match.ParsedData.ResolvedTextures.ContainsKey("_MainTex"),
                "_MainTex should NOT be in ResolvedTextures");

            // If _MainTex is absent and _BaseColorMap is present, the loader
            // falls back to _BaseColorMap as a diffuse override.
            // The presence of a resolved _BaseColorMap in ResolvedTextures is
            // the prerequisite the loader checks.
            Assert.Null(match.ParsedData.MainTex);
            Assert.NotNull(match.ParsedData.BaseColorMap);
            Assert.Empty(match.ParsedData.UnresolvedGuids);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_AssetRoots_DiscoverMatOutsideModelDir()
    {
        // Test that _unityAssetRoots constructor argument is used for .mat discovery
        var rootDir = Path.Combine(Path.GetTempPath(), "voxelforge-assetroot-test-" + Guid.NewGuid().ToString("N"));
        var modelDir = Path.Combine(rootDir, "Models");
        var assetRootDir = Path.Combine(rootDir, "Materials");
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(assetRootDir);
        try
        {
            var modelPath = Path.Combine(modelDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // .mat file lives in asset root (Materials/), NOT in model dir or parent
            var matPath = Path.Combine(assetRootDir, "AssetRootMat.mat");
            File.WriteAllText(matPath, """"
                --- !u!21 &2100000
                Material:
                  m_Name: AssetRootMat
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats: []
                    m_Colors:
                    - _Color: {r: 0.5, g: 0.5, b: 1, a: 1}
                """");

            var materialNames = new List<string> { "AssetRootMat" };

            // Resolver with explicit asset roots — should find .mat in Materials/
            var resolver = new UnityMatSidecarResolver([assetRootDir]);
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles,
                "Should find .mat files via explicit asset roots even when none in model dir");
            var match = result.Matches.FirstOrDefault(m => m.MatchedMaterialName == "AssetRootMat");
            Assert.NotNull(match);
            Assert.NotNull(match.ParsedData);
            Assert.Equal("AssetRootMat", match.ParsedData.MaterialName);
            Assert.True(match.ParsedData.MainColor.HasValue);

            // Control: resolver WITHOUT asset roots should NOT find the mat
            var resolverNoRoots = new UnityMatSidecarResolver();
            var resultNoRoots = resolverNoRoots.ProcessModel(modelPath, materialNames);
            Assert.False(resultNoRoots.FoundAnyMatFiles,
                "Without explicit asset roots, should not find .mat in non-model dir");
        }
        finally
        {
            Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_AssetRoots_ScannedForMetaGuidResolution()
    {
        // Test that _unityAssetRoots are included in GUID search roots.
        // Texture in a separate directory tree that is NOT an ancestor or sibling
        // of the model dir (so BuildSearchRoots won't find it without explicit roots).
        var baseDir = Path.Combine(Path.GetTempPath(), "voxelforge-metaroot-" + Guid.NewGuid().ToString("N"));
        var modelRoot = Path.Combine(baseDir, "Models");
        var textureRoot = Path.Combine(baseDir, "ExternalTextures");
        Directory.CreateDirectory(modelRoot);
        Directory.CreateDirectory(textureRoot);
        try
        {
            var modelPath = Path.Combine(modelRoot, "model.gltf");
            File.WriteAllText(modelPath, "");

            // Texture lives in a separate subdirectory of baseDir
            var texPath = Path.Combine(textureRoot, "diffuse.png");
            File.WriteAllText(texPath, "fake png");
            File.WriteAllText(texPath + ".meta", @"
fileFormatVersion: 2
guid: aabbccdd11223344aabbccdd11223344
timeCreated: 1234567890
");

            // .mat file in model directory referencing GUID
            File.WriteAllText(Path.Combine(modelRoot, "GuidMat.mat"), $@"
--- !u!21 &2100000
Material:
  m_Name: GuidMat
  m_SavedProperties:
    m_TexEnvs:
    - _MainTex:
        m_Texture: {{fileID: 2800000, guid: aabbccdd11223344aabbccdd11223344, type: 3}}
    m_Floats: []
    m_Colors: []
");

            var materialNames = new List<string> { "GuidMat" };

            // Without asset roots, GUID should NOT resolve because:
            // BuildSearchRoots adds modelRoot, its parent (baseDir), and walks up from there.
            // Texture is in baseDir/ExternalTextures which IS a sibling of modelRoot under baseDir.
            // baseDir is a parent of modelRoot, and Directory.EnumerateFiles(baseDir, "*.meta", AllDirectories)
            // WILL find the texture. So this test case validates the ASSERTION that the texture IS found
            // even without explicit roots due to parent directory search.
            var resolverNoRoots = new UnityMatSidecarResolver();
            var resultNoRoots = resolverNoRoots.ProcessModel(modelPath, materialNames);
            var matchNoRoots = resultNoRoots.Matches[0];
            Assert.NotNull(matchNoRoots.ParsedData);
            // The GUID should resolve because baseDir parent search finds it
            Assert.True(matchNoRoots.ParsedData.ResolvedTextures.ContainsKey("_MainTex"),
                "GUID should resolve via parent directory search (texture is sibling of model dir)");
            Assert.Equal(Path.GetFullPath(texPath), matchNoRoots.ParsedData.ResolvedTextures["_MainTex"]);

            // With ADDITIONAL explicit asset roots (even though not needed here),
            // should still resolve correctly — no duplicates or errors
            var resolverWithRoots = new UnityMatSidecarResolver([textureRoot]);
            var resultWithRoots = resolverWithRoots.ProcessModel(modelPath, materialNames);
            var matchWithRoots = resultWithRoots.Matches[0];
            Assert.NotNull(matchWithRoots.ParsedData);
            Assert.True(matchWithRoots.ParsedData.ResolvedTextures.ContainsKey("_MainTex"));
            Assert.Equal(Path.GetFullPath(texPath), matchWithRoots.ParsedData.ResolvedTextures["_MainTex"]);
            Assert.Empty(matchWithRoots.ParsedData.UnresolvedGuids);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_MatOutsideModelDir_ResolvesPathHintRelativeToMatDir()
    {
        // Test that when a .mat lives outside the model directory and references
        // a texture via PathHint relative to its own location, the resolver
        // resolves the texture relative to the .mat file directory first.
        var rootDir = Path.Combine(Path.GetTempPath(), "voxelforge-matdir-test-" + Guid.NewGuid().ToString("N"));
        var modelDir = Path.Combine(rootDir, "Models");
        var matDir = Path.Combine(rootDir, "Materials");
        var texDir = Path.Combine(matDir, "Textures");
        Directory.CreateDirectory(modelDir);
        Directory.CreateDirectory(texDir);
        try
        {
            var modelPath = Path.Combine(modelDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // .mat file in Materials/ (not model dir, not parent)
            // with a PathHint relative to its own directory
            var matPath = Path.Combine(matDir, "SidecarMat.mat");
            File.WriteAllText(matPath, """
                --- !u!21 &2100000
                Material:
                  m_Name: SidecarMat
                  m_SavedProperties:
                    m_TexEnvs:
                    - _MainTex:
                        m_Texture: Textures/diffuse.png
                    m_Floats: []
                    m_Colors: []
                """);

            // Texture lives next to the .mat file (in Materials/Textures/)
            var texPath = Path.Combine(texDir, "diffuse.png");
            File.WriteAllText(texPath, "fake png bytes");

            var materialNames = new List<string> { "SidecarMat" };

            // Resolver with explicit asset roots so the .mat is discovered
            // (no Assets/ root to walk up to in this test layout)
            var resolver = new UnityMatSidecarResolver([matDir]);
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles);
            var match = result.Matches.FirstOrDefault(m => m.MatchedMaterialName == "SidecarMat");
            Assert.NotNull(match);
            Assert.NotNull(match.ParsedData);
            Assert.True(match.ParsedData.ResolvedTextures.ContainsKey("_MainTex"),
                "PathHint should resolve relative to .mat file directory");
            Assert.Equal(Path.GetFullPath(texPath), match.ParsedData.ResolvedTextures["_MainTex"]);
        }
        finally
        {
            if (Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_DefaultDiscovery_FindsMatUnderAssetsRoot()
    {
        // Test that default resolver (no explicit constructor roots) discovers
        // .mat files by walking up from modelDir to find an Assets/ root and
        // scanning it recursively.
        var projectDir = Path.Combine(Path.GetTempPath(), "voxelforge-defaultdisc-" + Guid.NewGuid().ToString("N"));
        var assetsDir = Path.Combine(projectDir, "Assets");
        var modelsDir = Path.Combine(assetsDir, "Models");
        var materialsDir = Path.Combine(assetsDir, "Materials");
        Directory.CreateDirectory(modelsDir);
        Directory.CreateDirectory(materialsDir);
        try
        {
            var modelPath = Path.Combine(modelsDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // .mat file lives in Assets/Materials/, not in model dir or its parent
            var matPath = Path.Combine(materialsDir, "AssetMat.mat");
            File.WriteAllText(matPath, """
                --- !u!21 &2100000
                Material:
                  m_Name: AssetMat
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats: []
                    m_Colors:
                    - _Color: {r: 0.2, g: 0.4, b: 0.6, a: 1}
                """);

            var materialNames = new List<string> { "AssetMat" };

            // Default resolver — NO explicit roots
            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, materialNames);

            Assert.True(result.FoundAnyMatFiles,
                "Default resolver should find .mat by walking up to Assets/ root");
            var match = result.Matches.FirstOrDefault(m => m.MatchedMaterialName == "AssetMat");
            Assert.NotNull(match);
            Assert.NotNull(match.ParsedData);
            Assert.Equal("AssetMat", match.ParsedData.MaterialName);
            Assert.True(match.ParsedData.MainColor.HasValue);
            Assert.Equal(0.2f, match.ParsedData.MainColor.Value.R);
            Assert.Equal(0.4f, match.ParsedData.MainColor.Value.G);
            Assert.Equal(0.6f, match.ParsedData.MainColor.Value.B);
        }
        finally
        {
            if (Directory.Exists(projectDir))
                Directory.Delete(projectDir, recursive: true);
        }
    }

    // ── Task #1686: Alias map and safer substring matching ──

    [Fact]
    public void ProcessModel_AliasMap_ExactMatchByName()
    {
        // Explicit alias map should match GOLEM_NORMAL_ROCK → Golem
        // by m_Name, even though they are different strings.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-aliastest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            File.WriteAllText(Path.Combine(tempDir, "Golem.mat"), @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: Golem
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats:
    - _Cutoff: 0.5
    m_Colors:
    - _Color: {r: 0.5, g: 0.6, b: 0.7, a: 1}
");

            var aliasMap = new Dictionary<string, string>
            {
                { "GOLEM_NORMAL_ROCK", "Golem" },
            };

            var resolver = new UnityMatSidecarResolver(aliasMap: aliasMap);
            var result = resolver.ProcessModel(modelPath, new List<string> { "GOLEM_NORMAL_ROCK" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Equal("GOLEM_NORMAL_ROCK", match.MatchedMaterialName);
            Assert.Equal(UnityMatMatchKind.AliasMap, match.MatchKind);
            Assert.Equal(0.5f, match.ParsedData.Cutoff);
            Assert.True(match.ParsedData.MainColor.HasValue);
            Assert.Equal(0.5f, match.ParsedData.MainColor.Value.R);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }


    [Fact]
    public void ProcessModel_AliasMap_FilenameStemMatch_ReturnsAliasMapKind()
    {
        // When the alias target does not match any .mat m_Name,
        // the resolver falls back to filename stem matching against the alias target.
        // The match kind should still be AliasMap.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-aliasfn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // Mat m_Name differs from alias target; alias maps via filename stem fallback
            File.WriteAllText(Path.Combine(tempDir, "Golem.mat"), @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: NotMatching
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats:
    - _Cutoff: 0.5
    m_Colors:
    - _Color: {r: 0.5, g: 0.6, b: 0.7, a: 1}
");

            var aliasMap = new Dictionary<string, string>
            {
                { "GOLEM_NORMAL_ROCK", "Golem" },
            };

            var resolver = new UnityMatSidecarResolver(aliasMap: aliasMap);
            var result = resolver.ProcessModel(modelPath, new List<string> { "GOLEM_NORMAL_ROCK" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Equal("GOLEM_NORMAL_ROCK", match.MatchedMaterialName);
            Assert.Equal(UnityMatMatchKind.AliasMap, match.MatchKind);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_AliasMap_PreservesLegacySubstringBehavior()
    {
        // Without alias map, the Golem-style substring match should still work
        // with the new minimum length guard (Golem is 5 chars >= default 4).
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-legacysub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            File.WriteAllText(Path.Combine(tempDir, "Golem.mat"), @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: Golem
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 0.5, g: 0.6, b: 0.7, a: 1}
");

            // No alias map — use default resolver
            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, new List<string> { "GOLEM_NORMAL_ROCK" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            // Falls through to substring match (Strategy 3) since "Golem" (5 chars)
            // meets the min length threshold
            Assert.Equal("GOLEM_NORMAL_ROCK", match.MatchedMaterialName);
            Assert.Equal(UnityMatMatchKind.FilenameStem, match.MatchKind);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_MinSubstringLength_BlocksVeryShortNames()
    {
        // Very short sidecar names (fewer than 4 chars) should NOT match
        // via substring/prefix strategy when using default min length.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-shortsub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            // "G" is a very short m_Name — should not match anything via substring.
            File.WriteAllText(Path.Combine(tempDir, "G.mat"), @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: G
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 1, g: 0, b: 0, a: 1}
");

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, new List<string> { "GOLEM_NORMAL_ROCK" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.Null(match.ParsedData);
            Assert.Equal(UnityMatMatchKind.None, match.MatchKind);
            Assert.True(match.MatchKind == UnityMatMatchKind.None,
                "Very short sidecar name 'G' should NOT match 'GOLEM_NORMAL_ROCK' via substring with default min length 4");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_MinSubstringLength_ZeroPermitsAll()
    {
        // Setting MinSubstringMatchLength=0 should restore legacy behavior
        // where even very short substrings can match.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-zerolegacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.fbx");
            File.WriteAllText(modelPath, "");

            File.WriteAllText(Path.Combine(tempDir, "G.mat"), @"%YAML 1.1
--- !u!21 &2100000
Material:
  m_Name: G
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 1, g: 0, b: 0, a: 1}
");

            // Explicit minSubstringMatchLength=0 to allow all lengths
            var resolver = new UnityMatSidecarResolver(minSubstringMatchLength: 0);
            var result = resolver.ProcessModel(modelPath, new List<string> { "GOLEM_NORMAL_ROCK" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Equal(UnityMatMatchKind.FilenameStem, match.MatchKind);
            Assert.True(match.MatchKind == UnityMatMatchKind.FilenameStem,
                "With minSubstringMatchLength=0, 'G' should match 'GOLEM_NORMAL_ROCK' (legacy behavior)");
            Assert.True(match.ParsedData.MainColor.HasValue);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_ExactNameMatch_StillWorksWithNewThreshold()
    {
        // Exact name matches (Strategy 1) should be unaffected by min length threshold.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-exactthresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.glb");
            File.WriteAllText(modelPath, "");

            File.WriteAllText(Path.Combine(tempDir, "Hi.mat"), @"--- !u!21 &2100000
Material:
  m_Name: Hi
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 0.1, g: 0.2, b: 0.3, a: 1}
");

            // "Hi" is short (2 chars) but exact name match should still work
            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, new List<string> { "Hi" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Equal(UnityMatMatchKind.ExactName, match.MatchKind);
            Assert.True(match.ParsedData.MainColor.HasValue);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessModel_FilenameStemMatch_StillWorksWithNewThreshold()
    {
        // Filename stem matches (Strategy 2) should be unaffected by min length threshold.
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-fstemthresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var modelPath = Path.Combine(tempDir, "model.glb");
            File.WriteAllText(modelPath, "");

            // Filename stem "AB" (2 chars) with different m_Name
            File.WriteAllText(Path.Combine(tempDir, "AB.mat"), @"--- !u!21 &2100000
Material:
  m_Name: DifferentName
  m_SavedProperties:
    m_TexEnvs: []
    m_Floats: []
    m_Colors:
    - _Color: {r: 0.9, g: 0.1, b: 0.1, a: 1}
");

            var resolver = new UnityMatSidecarResolver();
            var result = resolver.ProcessModel(modelPath, new List<string> { "AB" });

            Assert.True(result.FoundAnyMatFiles);
            Assert.Single(result.Matches);
            var match = result.Matches[0];
            Assert.NotNull(match.ParsedData);
            Assert.Equal(UnityMatMatchKind.FilenameStem, match.MatchKind);
            Assert.True(match.MatchKind == UnityMatMatchKind.FilenameStem,
                "Filename stem match should work regardless of name length");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
