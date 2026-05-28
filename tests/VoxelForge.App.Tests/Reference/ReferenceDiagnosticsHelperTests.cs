using VoxelForge.App.Reference;
using VoxelForge.Core.Reference;

namespace VoxelForge.App.Tests.Reference;

public sealed class ReferenceDiagnosticsHelperTests
{
    [Fact]
    public void AddUnityMatSidecarDiagnostics_MixedProvenance_ReportsBothSources()
    {
        // Simulate a scenario where Unity sidecar contributes the diffuse texture
        // and .vf-reference-settings.json contributes sampling controls,
        // creating mixed provenance on the same mesh.
        var warnings = new List<DiagnosticWarning>();

        var sidecarResult = new UnityMatSidecarResult
        {
            FoundAnyMatFiles = true,
            Matches =
            [
                new UnityMatMatchResult
                {
                    MatFilePath = "/model/Materials/TestMat.mat",
                    MatchedMaterialName = "TestMaterial",
                    MatchKind = UnityMatMatchKind.ExactName,
                    ParsedData = new UnityMatData
                    {
                        MaterialName = "TestMaterial",
                        MainTex = new UnityTextureRef { Guid = "abcdef1234567890abcdef1234567890" },
                        MainColor = new UnityVector4(1, 0, 0, 1),
                        ResolvedTextures = { ["_MainTex"] = "/model/Textures/diffuse.png" },
                    },
                    Warnings = [],
                },
            ],
            GlobalWarnings = [],
        };

        // Mesh with Unity sidecar texture but vf_reference_settings sampling
        var meshes = new List<ReferenceMeshData>
        {
            new()
            {
                Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 0, 0, 255)],
                Indices = [0, 0, 0],
                MaterialName = "TestMaterial",
                DiffuseTexturePath = "/model/Textures/diffuse.png",
                DiffuseTextureSource = "unity_sidecar",
                SamplingControlsSource = "vf_reference_settings",
                UvOrigin = "top_left",
                FlipY = "asset_defined",
                WrapS = "clamp",
                WrapT = "clamp",
            },
        };

        ReferenceDiagnosticsHelper.AddUnityMatSidecarDiagnostics(
            warnings, sidecarResult, meshes: meshes);

        // Should have at least the standard sidecar diagnostics plus mixed provenance
        Assert.Contains(warnings, w => w.Code == "unity_mat_sidecars_matched");
        Assert.Contains(warnings, w => w.Code == "mixed_provenance_detected");
        Assert.Contains(warnings, w => w.Code == "mixed_provenance_per_mesh");

        var mixedDiag = warnings.First(w => w.Code == "mixed_provenance_per_mesh");
        Assert.Contains("TestMaterial", mixedDiag.Message, StringComparison.Ordinal);
        Assert.Contains("Unity .mat sidecar", mixedDiag.Message, StringComparison.Ordinal);
        Assert.Contains("vf-reference-settings.json", mixedDiag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddUnityMatSidecarDiagnostics_NoMeshProvenanceMixing_OmitsMixedDiagnostics()
    {
        // When all sources agree (texture and sampling from same source), no mixed diagnostics.
        var warnings = new List<DiagnosticWarning>();

        var sidecarResult = new UnityMatSidecarResult
        {
            FoundAnyMatFiles = true,
            Matches =
            [
                new UnityMatMatchResult
                {
                    MatFilePath = "/model/Materials/Simple.mat",
                    MatchedMaterialName = "Simple",
                    MatchKind = UnityMatMatchKind.ExactName,
                    ParsedData = new UnityMatData
                    {
                        MaterialName = "Simple",
                    },
                    Warnings = [],
                },
            ],
            GlobalWarnings = [],
        };

        // Mesh where both texture and sampling come from unity_sidecar
        var meshes = new List<ReferenceMeshData>
        {
            new()
            {
                Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 128, 128, 128, 255)],
                Indices = [0, 0, 0],
                MaterialName = "Simple",
                DiffuseTexturePath = "/model/Textures/tex.png",
                DiffuseTextureSource = "unity_sidecar",
                SamplingControlsSource = "unity_sidecar",
            },
        };

        ReferenceDiagnosticsHelper.AddUnityMatSidecarDiagnostics(
            warnings, sidecarResult, meshes: meshes);

        // Standard sidecar diagnostics should be present
        Assert.Contains(warnings, w => w.Code == "unity_mat_sidecars_matched");
        // Mixed provenance diagnostics should NOT be present
        Assert.DoesNotContain(warnings, w => w.Code == "mixed_provenance_detected");
        Assert.DoesNotContain(warnings, w => w.Code == "mixed_provenance_per_mesh");
    }

    [Fact]
    public void AddUnityMatSidecarDiagnostics_MixedEmissiveProvenance_Reports()
    {
        var warnings = new List<DiagnosticWarning>();

        var sidecarResult = new UnityMatSidecarResult
        {
            FoundAnyMatFiles = true,
            Matches =
            [
                new UnityMatMatchResult
                {
                    MatFilePath = "/model/Materials/Glow.mat",
                    MatchedMaterialName = "Glow",
                    MatchKind = UnityMatMatchKind.FilenameStem,
                    ParsedData = new UnityMatData
                    {
                        MaterialName = "Glow",
                        EmissionMap = new UnityTextureRef { Guid = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee" },
                        ResolvedTextures = { ["_EmissionMap"] = "/model/Textures/glow.png" },
                    },
                    Warnings = [],
                },
            ],
            GlobalWarnings = [],
        };

        var meshes = new List<ReferenceMeshData>
        {
            new()
            {
                Vertices = [new ReferenceVertex(0, 0, 0, 0, 1, 0, 255, 255, 255, 255)],
                Indices = [0, 0, 0],
                MaterialName = "Glow",
                DiffuseTexturePath = "/model/Textures/tex.png",
                DiffuseTextureSource = "assimp",
                EmissiveTexturePath = "/model/Textures/glow.png",
                EmissiveTextureSource = "unity_sidecar",
                SamplingControlsSource = "assimp",
            },
        };

        ReferenceDiagnosticsHelper.AddUnityMatSidecarDiagnostics(
            warnings, sidecarResult, meshes: meshes);

        Assert.Contains(warnings, w => w.Code == "emissive_provenance_mix");
        var emDiag = warnings.First(w => w.Code == "emissive_provenance_mix");
        Assert.Contains("Glow", emDiag.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddUnityMatSidecarDiagnostics_NullMeshes_DoesNotThrow()
    {
        // Passing null meshes should not throw and should produce standard diagnostics.
        var warnings = new List<DiagnosticWarning>();

        var sidecarResult = new UnityMatSidecarResult
        {
            FoundAnyMatFiles = false,
            Matches = [],
            GlobalWarnings = [],
        };

        // This should not throw
        ReferenceDiagnosticsHelper.AddUnityMatSidecarDiagnostics(
            warnings, sidecarResult, meshes: null);

        Assert.Contains(warnings, w => w.Code == "no_unity_mat_sidecars");
    }
}
