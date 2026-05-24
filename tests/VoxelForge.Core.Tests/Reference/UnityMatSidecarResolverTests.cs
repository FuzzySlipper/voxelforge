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
}
