using System.Text.Json;
using VoxelForge.Core.Reference;

namespace VoxelForge.Core.Tests.Reference;

public sealed class UnityMatParserTests
{
    [Fact]
    public void Parse_StandardUnityMat_YieldsMaterialsAndTextures()
    {
        // A standard Unity Lit/URP material YAML
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!21 &2100000
            Material:
              serializedVersion: 6
              m_Name: ExampleMaterial
              m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}
              m_ShaderKeywords:
              m_LightmapFlags: 4
              m_EnableInstancingVariants: 0
              m_DoubleSidedGI: 0
              m_CustomRenderQueue: -1
              stringTagMap: {}
              disabledShaderPasses: []
              m_SavedProperties:
                serializedVersion: 3
                m_TexEnvs:
                - _MainTex:
                    m_Texture: {fileID: 2800000, guid: abcdef1234567890abcdef1234567890, type: 3}
                    m_Scale: {x: 1, y: 1}
                    m_Offset: {x: 0, y: 0}
                - _BumpMap:
                    m_Texture: {fileID: 2800000, guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}
                    m_Scale: {x: 1, y: 1}
                    m_Offset: {x: 0, y: 0}
                m_Floats:
                - _Cutoff: 0.5
                - _Glossiness: 0.4
                - _Metallic: 1
                - _ZWrite: 1
                m_Colors:
                - _Color: {r: 0.8, g: 0.2, b: 0.2, a: 1}
                - _EmissionColor: {r: 0.5, g: 0.1, b: 0, a: 1}
            """;

        var parsed = UnityMatParser.Parse(yaml, "/path/to/ExampleMaterial.mat");

        Assert.NotNull(parsed);
        Assert.Equal("ExampleMaterial", parsed.MaterialName);

        // Texture slots
        Assert.NotNull(parsed.MainTex);
        Assert.Equal("abcdef1234567890abcdef1234567890", parsed.MainTex.Guid);
        Assert.Equal(2800000, parsed.MainTex.FileId);
        Assert.Equal(3, parsed.MainTex.Type);

        Assert.Null(parsed.BaseColorMap);
        Assert.Null(parsed.EmissionMap);

        // Colors
        Assert.True(parsed.MainColor.HasValue);
        Assert.Equal(0.8f, parsed.MainColor.Value.R);
        Assert.Equal(0.2f, parsed.MainColor.Value.G);
        Assert.Equal(0.2f, parsed.MainColor.Value.B);
        Assert.Equal(1f, parsed.MainColor.Value.A);

        Assert.True(parsed.EmissionColor.HasValue);
        Assert.Equal(0.5f, parsed.EmissionColor.Value.R);
        Assert.Equal(0.1f, parsed.EmissionColor.Value.G);
        Assert.Equal(0f, parsed.EmissionColor.Value.B);

        // Scalars
        Assert.Equal(0.5f, parsed.Cutoff);
        Assert.Equal(0.4f, parsed.Glossiness);
        Assert.Equal(1f, parsed.Metallic);

        // Ignored properties should include _BumpMap and _ZWrite
        Assert.Contains("_BumpMap", parsed.IgnoredProperties, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("_ZWrite", parsed.IgnoredProperties, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_HdrpLitMaterial_HandlesBaseMapAndBaseColor()
    {
        var yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!21 &21000000
            Material:
              serializedVersion: 6
              m_Name: HDRPMaterial
              m_SavedProperties:
                serializedVersion: 3
                m_TexEnvs:
                - _BaseMap:
                    m_Texture: {fileID: 2800000, guid: cccccccccccccccccccccccccccccccc, type: 3}
                - _BaseColorMap:
                    m_Texture: {fileID: 2800000, guid: dddddddddddddddddddddddddddddddd, type: 3}
                - _EmissionMap:
                    m_Texture: {fileID: 2800000, guid: eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee, type: 3}
                m_Floats:
                - _Cutoff: 0.33
                m_Colors:
                - _BaseColor: {r: 1, g: 0.5, b: 0.25, a: 1}
                - _EmissionColor: {r: 2, g: 1, b: 0.5, a: 1}
            """;

        var parsed = UnityMatParser.Parse(yaml);

        Assert.NotNull(parsed);
        Assert.Equal("HDRPMaterial", parsed.MaterialName);

        // _BaseMap and _MainTex both map to MainTex
        Assert.NotNull(parsed.MainTex);
        Assert.Equal("cccccccccccccccccccccccccccccccc", parsed.MainTex.Guid);

        // _BaseColorMap is separate
        Assert.NotNull(parsed.BaseColorMap);
        Assert.Equal("dddddddddddddddddddddddddddddddd", parsed.BaseColorMap.Guid);

        // Emission map
        Assert.NotNull(parsed.EmissionMap);
        Assert.Equal("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee", parsed.EmissionMap.Guid);

        // _BaseColor maps to MainColor
        Assert.True(parsed.MainColor.HasValue);
        Assert.Equal(0.5f, parsed.MainColor.Value.G);

        // Emission color with >1 values
        Assert.True(parsed.EmissionColor.HasValue);
        Assert.Equal(2f, parsed.EmissionColor.Value.R);

        Assert.Equal(0.33f, parsed.Cutoff);
    }

    [Fact]
    public void Parse_MinimalMat_NoTexturesUsefulDefaults()
    {
        var yaml = """
            --- !u!21 &2100000
            Material:
              m_Name: BareMinimum
              m_SavedProperties:
                serializedVersion: 3
                m_TexEnvs: []
                m_Floats: []
                m_Colors: []
            """;

        var parsed = UnityMatParser.Parse(yaml);

        Assert.NotNull(parsed);
        Assert.Equal("BareMinimum", parsed.MaterialName);
        Assert.Null(parsed.MainTex);
        Assert.Null(parsed.MainColor);
        Assert.Null(parsed.Cutoff);
        Assert.Null(parsed.Glossiness);
        Assert.Null(parsed.Metallic);
        Assert.Empty(parsed.IgnoredProperties);
        Assert.Empty(parsed.UnresolvedGuids);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsNull()
    {
        Assert.Null(UnityMatParser.Parse(""));
        Assert.Null(UnityMatParser.Parse("   "));
        Assert.Null(UnityMatParser.Parse(null!));
    }

    [Fact]
    public void Parse_NoMaterialBlock_ReturnsDataButNoName()
    {
        var yaml = """
            %YAML 1.1
            Something: else
            """;

        var parsed = UnityMatParser.Parse(yaml);
        Assert.NotNull(parsed);
        Assert.Null(parsed.MaterialName);
    }

    [Fact]
    public void Parse_SmoothnessMapsToGlossiness()
    {
        var yaml = """
            --- !u!21 &2100000
            Material:
              m_Name: SmoothMat
              m_SavedProperties:
                m_Floats:
                - _Smoothness: 0.75
                m_Colors: []
                m_TexEnvs: []
            """;

        var parsed = UnityMatParser.Parse(yaml);

        Assert.NotNull(parsed);
        Assert.Equal(0.75f, parsed.Glossiness);
    }

    [Fact]
    public void Parse_TextureWithMissingFields_ResolvesPartial()
    {
        var yaml = """
            --- !u!21 &2100000
            Material:
              m_Name: PartialTexMat
              m_SavedProperties:
                m_TexEnvs:
                - _MainTex:
                    m_Texture: {fileID: 2800000, guid: abcdef1234567890abcdef1234567890, type: 3}
                m_Floats: []
                m_Colors:
                - _Color: {r: 1, g: 1, b: 1, a: 1}
            """;

        var parsed = UnityMatParser.Parse(yaml);

        Assert.NotNull(parsed);
        Assert.NotNull(parsed.MainTex);
        Assert.Equal("abcdef1234567890abcdef1234567890", parsed.MainTex.Guid);
        Assert.True(parsed.MainColor.HasValue);
    }

    [Fact]
    public void FromFile_NonExistentFile_ReturnsNull()
    {
        var result = UnityMatParser.ParseFile("/nonexistent/path.mat");
        Assert.Null(result);
    }

    [Fact]
    public void ParseFile_ValidMatFile_ReturnsParsedData()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "test_unity_mat_" + Guid.NewGuid().ToString("N") + ".mat");
        try
        {
            File.WriteAllText(tempPath, """
                %YAML 1.1
                --- !u!21 &2100000
                Material:
                  m_Name: DiskMat
                  m_SavedProperties:
                    m_TexEnvs: []
                    m_Floats:
                    - _Cutoff: 0.5
                    m_Colors: []
                """);

            var parsed = UnityMatParser.ParseFile(tempPath);
            Assert.NotNull(parsed);
            Assert.Equal("DiskMat", parsed.MaterialName);
            Assert.Equal(0.5f, parsed.Cutoff);
            Assert.Equal(tempPath, parsed.SourceFilePath);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
