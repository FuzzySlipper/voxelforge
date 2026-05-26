using Microsoft.Extensions.Logging.Abstractions;
using StbImageSharp;
using VoxelForge.Content;
using VoxelForge.Core.Reference;

namespace VoxelForge.Mcp.Tests;

/// <summary>
/// Tests for sampling-aware texture sampling and emissive contribution in the bake path.
/// Verify that SampleTexture uses per-mesh UV origin, flip_y, wrap_s, wrap_t controls
/// instead of hardcoded repeat + V flip, and that emissive contribution works correctly.
/// </summary>
public sealed class ReferenceModelLoaderSamplingTests
{
    /// <summary>
    /// Create a tiny 4x4 RGBA image for testing with known pixel values.
    /// Pixel layout (row-major):
    ///   (0,0)=red, (1,0)=green, (2,0)=blue, (3,0)=white
    ///   (0,1)=yellow, (1,1)=cyan, (2,1)=magenta, (3,1)=black
    ///   (0,2)=gray50, (1,2)=gray75, (2,2)=gray25, (3,2)=gray10
    ///   (0,3)=red_lo, (1,3)=green_lo, (2,3)=blue_lo, (3,3)=white_lo
    /// </summary>
    private static ImageResult CreateTestTexture()
    {
        var data = new byte[4 * 4 * 4];
        // Row 0 (y=0)
        data[0] = 255; data[1] = 0; data[2] = 0; data[3] = 255;     // (0,0)=red
        data[4] = 0; data[5] = 255; data[6] = 0; data[7] = 255;     // (1,0)=green
        data[8] = 0; data[9] = 0; data[10] = 255; data[11] = 255;   // (2,0)=blue
        data[12] = 255; data[13] = 255; data[14] = 255; data[15] = 255; // (3,0)=white
        // Row 1 (y=1)
        data[16] = 255; data[17] = 255; data[18] = 0; data[19] = 255;   // (0,1)=yellow
        data[20] = 0; data[21] = 255; data[22] = 255; data[23] = 255;   // (1,1)=cyan
        data[24] = 255; data[25] = 0; data[26] = 255; data[27] = 255;   // (2,1)=magenta
        data[28] = 0; data[29] = 0; data[30] = 0; data[31] = 255;       // (3,1)=black
        // Row 2 (y=2)
        data[32] = 128; data[33] = 128; data[34] = 128; data[35] = 255; // (0,2)=gray50
        data[36] = 192; data[37] = 192; data[38] = 192; data[39] = 255; // (1,2)=gray75
        data[40] = 64; data[41] = 64; data[42] = 64; data[43] = 255;    // (2,2)=gray25
        data[44] = 26; data[45] = 26; data[46] = 26; data[47] = 255;    // (3,2)=gray10
        // Row 3 (y=3)
        data[48] = 200; data[49] = 0; data[50] = 0; data[51] = 255;     // (0,3)=red_lo
        data[52] = 0; data[53] = 200; data[54] = 0; data[55] = 255;     // (1,3)=green_lo
        data[56] = 0; data[57] = 0; data[58] = 200; data[59] = 255;     // (2,3)=blue_lo
        data[60] = 200; data[61] = 200; data[62] = 200; data[63] = 255; // (3,3)=white_lo

        return new ImageResult
        {
            Width = 4,
            Height = 4,
            Data = data,
        };
    }

    [Fact]
    public void SampleTexture_DefaultParams_RepeatWrappingAndFlipV()
    {
        // Default parameters: top_left, asset_defined => flip V, repeat wrapping
        var tex = CreateTestTexture();

        // UV (0.25, 0.375) with top_left origin and asset_defined flip_y:
        //   - wrap: repeat -> 0.25, 0.375
        //   - flip_y=asset_defined + origin=top_left => flip V: v = 1 - 0.375 = 0.625
        //   - px = (int)(0.25 * 4) = 1, py = (int)(0.625 * 4) = 2
        //   - pixel (1, 2) = gray75 (192, 192, 192)
        var loader = CreateLoader();
        // Use reflection to call the private static method
        InvokeSampleTexture(tex, 0.25f, 0.375f, out byte r, out byte g, out byte b, out byte a);
        Assert.Equal(192, r);
        Assert.Equal(192, g);
        Assert.Equal(192, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SampleTexture_BottomLeft_NoFlipV()
    {
        var tex = CreateTestTexture();

        // UV (0.25, 0.25) with bottom_left origin and asset_defined flip_y:
        //   - wrap: repeat -> 0.25, 0.25
        //   - flip_y=asset_defined + origin=bottom_left => no flip: v = 0.25
        //   - px = (int)(0.25 * 4) = 1, py = (int)(0.25 * 4) = 1
        //   - pixel (1, 1) = cyan (0, 255, 255)
        InvokeSampleTexture(tex, 0.25f, 0.25f, out byte r, out byte g, out byte b, out byte a,
            uvOrigin: "bottom_left");
        Assert.Equal(0, r);
        Assert.Equal(255, g);
        Assert.Equal(255, b);
        Assert.Equal(255, a);
    }

    [Fact]
    public void SampleTexture_ClampWrapping_ClampsOutOfRange()
    {
        var tex = CreateTestTexture();

        // UV (1.5, 1.5) with clamp wrapping:
        //   - wrap: clamp -> u = 1.0, v = 1.0
        //   - flip_y=asset_defined + origin=top_left => flip: v = 1 - 1.0 = 0.0
        //   - px = (int)(1.0 * 4) = 3 (clamped to 3), py = (int)(0.0 * 4) = 0
        //   - pixel (3, 0) = white (255, 255, 255)
        InvokeSampleTexture(tex, 1.5f, 1.5f, out byte r, out byte g, out byte b, out byte a,
            uvOrigin: "top_left", flipY: "false", wrapS: "clamp", wrapT: "clamp");
        // With clamp: u=Clamp(1.5, 0, 1)=1.0, v=Clamp(1.5, 0, 1)=1.0
        // No flip because flipY=false
        // px = (int)(1.0 * 4) = 3, py = (int)(1.0 * 4) = 3
        // pixel (3, 3) = white_lo (200, 200, 200)
        Assert.Equal(200, r);
        Assert.Equal(200, g);
        Assert.Equal(200, b);
    }

    [Fact]
    public void SampleTexture_MirrorWrapping_ReflectsCoordinates()
    {
        var tex = CreateTestTexture();

        // UV (1.25, 0.5) with mirror wrapping:
        //   - wrap: mirror -> u=1.25: floor=1, period=1 (odd) => wrapped = 1 - 0.25 = 0.75
        //   - v=0.5: floor=0, period=0 (even) => wrapped = 0.5
        //   - flip_y=asset_defined + origin=top_left => flip: v = 1 - 0.5 = 0.5
        //   - px = (int)(0.75 * 4) = 3, py = (int)(0.5 * 4) = 2
        //   - pixel (3, 2) = gray10 (26, 26, 26)
        InvokeSampleTexture(tex, 1.25f, 0.5f, out byte r, out byte g, out byte b, out byte a,
            uvOrigin: "top_left", flipY: "false", wrapS: "mirror", wrapT: "mirror");
        Assert.Equal(26, r);
        Assert.Equal(26, g);
        Assert.Equal(26, b);
    }

    [Fact]
    public void SampleTexture_FlipYTrue_AlwaysFlips()
    {
        var tex = CreateTestTexture();

        // UV (0.25, 0.25) with flip_y=true:
        //   - wrap: repeat -> 0.25, 0.25
        //   - flip_y=true => flip: v = 1 - 0.25 = 0.75
        //   - px = 1, py = 3 => pixel (1, 3) = green_lo (0, 200, 0)
        InvokeSampleTexture(tex, 0.25f, 0.25f, out byte r, out byte g, out byte b, out byte a,
            uvOrigin: "bottom_left", flipY: "true");
        Assert.Equal(0, r);
        Assert.Equal(200, g);
        Assert.Equal(0, b);
    }

    [Fact]
    public void SampleTexture_FlipYFalse_NeverFlips()
    {
        var tex = CreateTestTexture();

        // UV (0.25, 0.25) with flip_y=false, bottom_left:
        //   - no flip regardless of origin
        //   - px = 1, py = 1 => pixel (1, 1) = cyan (0, 255, 255)
        InvokeSampleTexture(tex, 0.25f, 0.25f, out byte r, out byte g, out byte b, out byte a,
            uvOrigin: "bottom_left", flipY: "false");
        Assert.Equal(0, r);
        Assert.Equal(255, g);
        Assert.Equal(255, b);
    }

    [Fact]
    public void BakeVertexColors_AppliesEmissiveContribution()
    {
        // Test that emissive texture contribution is correctly applied to baked vertex colors
        // using the same sampling controls as the diffuse pass
        var loader = CreateLoader();
        var diffuseTex = CreateTestTexture();
        var emissiveTex = CreateTestTexture();

        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 128, 128, 128, 255, 0.25f, 0.25f),
            new ReferenceVertex(1, 0, 0, 0, 0, 1, 128, 128, 128, 255, 0.5f, 0.5f),
            new ReferenceVertex(0, 1, 0, 0, 0, 1, 128, 128, 128, 255, 0.75f, 0.75f),
        };

        // With default sampling (top_left, asset_defined => flip V) and emissive brightness 0.5:
        // For vertex 0: uv=(0.25, 0.25) -> after flip: v=0.75 -> diffuse pixel(1,3)=green_lo(0,200,0)
        //   emissive: same pixel -> (0,200,0) * 0.5 = (0,100,0)
        //   clamped: (0+0, 200+100, 0+0) = (0, 255, 0)
        // For vertex 1: uv=(0.5, 0.5) -> after flip: v=0.5 -> diffuse pixel(2,2)=gray25(64,64,64)
        //   emissive: same pixel -> (64,64,64) * 0.5 = (32,32,32)
        //   clamped: (64+32, 64+32, 64+32) = (96, 96, 96)
        // For vertex 2: uv=(0.75, 0.75) -> after flip: v=0.25 -> diffuse pixel(3,1)=black(0,0,0)
        //   emissive: same pixel -> (0,0,0) * 0.5 = (0,0,0)
        //   clamped: (0+0, 0+0, 0+0) = (0, 0, 0)

        var result = InvokeBakeVertexColors(vertices, diffuseTex, emissiveTex, 0.5f,
            uvOrigin: "top_left", flipY: "asset_defined");

        // Vertex 0: diffuse green(0,200,0) + emissive green*0.5(0,100,0) = (0, 255, 0)
        Assert.Equal(byte.MinValue, result[0].R); // 0
        Assert.Equal(byte.MaxValue, result[0].G); // 255
        Assert.Equal(byte.MinValue, result[0].B); // 0

        // Vertex 1: diffuse gray25(64,64,64) + emissive gray25*0.5(32,32,32) = (96,96,96)
        Assert.Equal(96, result[1].R);
        Assert.Equal(96, result[1].G);
        Assert.Equal(96, result[1].B);

        // Vertex 2: diffuse black(0,0,0) + emissive black*0.5(0,0,0) = (0,0,0)
        Assert.Equal(byte.MinValue, result[2].R);
        Assert.Equal(byte.MinValue, result[2].G);
        Assert.Equal(byte.MinValue, result[2].B);
    }

    [Fact]
    public void BakeVertexColors_WithClampAndBottomLeft_EmissiveUsesSameControls()
    {
        var loader = CreateLoader();
        var diffuseTex = CreateTestTexture();
        var emissiveTex = CreateTestTexture();

        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 128, 128, 128, 255, 0.25f, 0.25f),
        };

        // bottom_left origin, asset_defined flip_y => no flip (because origin is bottom_left)
        // wrap=clamp
        // uv=(0.25, 0.25) -> clamp(0.25, 0, 1)=0.25 -> no flip -> px=1, py=1 -> cyan(0,255,255)
        // emissive: same -> (0,255,255) * 1.0 = (0,255,255)
        // clamped: (0+0, 255+255, 255+255) = (0, 255, 255) each component clamped to 255

        var result = InvokeBakeVertexColors(vertices, diffuseTex, emissiveTex, 1.0f,
            uvOrigin: "bottom_left", flipY: "asset_defined", wrapS: "clamp", wrapT: "clamp");

        Assert.Equal(byte.MinValue, result[0].R); // 0 + 0 = 0
        Assert.Equal(byte.MaxValue, result[0].G); // 255 + 255 = clamped to 255
        Assert.Equal(byte.MaxValue, result[0].B); // 255 + 255 = clamped to 255
    }

    [Fact]
    public void BlendEmissive_AppliesWithBrightness()
    {
        var tex = CreateTestTexture();

        var vertices = new[]
        {
            new ReferenceVertex(0, 0, 0, 0, 0, 1, 100, 50, 30, 255, 0.25f, 0.5f),
        };

        // uv=(0.25, 0.5), top_left, asset_defined => flip: v=0.5 => px=1, py=2 => gray75(192,192,192)
        // emissive: (192,192,192) * 2.0 brightness = (384,384,384) -> clamped to 255
        // base: (100, 50, 30) + (255, 255, 255) = (255, 255, 255)

        var result = InvokeBlendEmissive(vertices, tex, 2.0f,
            uvOrigin: "top_left", flipY: "asset_defined");

        Assert.Equal(byte.MaxValue, result[0].R);
        Assert.Equal(byte.MaxValue, result[0].G);
        Assert.Equal(byte.MaxValue, result[0].B);
    }

    [Fact]
    public void RetexturePreservesSamplingControlsAndManualOverrides()
    {
        var loader = CreateLoader();
        var texturePath = WriteTinyPng();
        try
        {
            var mesh = new ReferenceMeshData
            {
                Vertices =
                [
                    new ReferenceVertex(0, 0, 0, 0, 0, 1, 10, 20, 30, 255, 0.5f, 0.5f),
                ],
                Indices = [0],
                MaterialName = "mat",
                DiffuseTexturePath = texturePath,
                EmissiveTexturePath = texturePath,
                EmissiveBrightness = 0.25f,
                DiffuseTextureSource = "unity_sidecar",
                EmissiveTextureSource = "unity_sidecar",
                ManualDiffuseOverridePath = texturePath,
                ManualNormalOverridePath = texturePath,
                ManualEmissiveOverridePath = texturePath,
                UvOrigin = "bottom_left",
                FlipY = "asset_defined",
                WrapS = "clamp",
                WrapT = "mirror",
                SamplingControlsSource = "manual_sampling_override",
            };

            var diffuse = loader.Retexture(mesh, texturePath);
            Assert.NotNull(diffuse);
            Assert.Equal("bottom_left", diffuse.UvOrigin);
            Assert.Equal("asset_defined", diffuse.FlipY);
            Assert.Equal("clamp", diffuse.WrapS);
            Assert.Equal("mirror", diffuse.WrapT);
            Assert.Equal("manual_sampling_override", diffuse.SamplingControlsSource);
            Assert.Equal(texturePath, diffuse.ManualDiffuseOverridePath);
            Assert.Equal(texturePath, diffuse.ManualNormalOverridePath);
            Assert.Equal(texturePath, diffuse.ManualEmissiveOverridePath);

            var emissive = loader.RetextureEmissive(mesh, texturePath, 1f);
            Assert.NotNull(emissive);
            Assert.Equal("bottom_left", emissive.UvOrigin);
            Assert.Equal("asset_defined", emissive.FlipY);
            Assert.Equal("clamp", emissive.WrapS);
            Assert.Equal("mirror", emissive.WrapT);
            Assert.Equal("manual_sampling_override", emissive.SamplingControlsSource);
            Assert.Equal(texturePath, emissive.ManualDiffuseOverridePath);
            Assert.Equal(texturePath, emissive.ManualNormalOverridePath);
            Assert.Equal(texturePath, emissive.ManualEmissiveOverridePath);
        }
        finally
        {
            File.Delete(texturePath);
        }
    }

    // ── Reflection-based helpers to access private static methods ──

    private static ReferenceModelLoader CreateLoader()
        => new(NullLogger<ReferenceModelLoader>.Instance);

    private static string WriteTinyPng()
    {
        var path = Path.Combine(Path.GetTempPath(), $"voxelforge-texture-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII="));
        return path;
    }

    private static void InvokeSampleTexture(ImageResult image, float u, float v,
        out byte r, out byte g, out byte b, out byte a,
        string uvOrigin = "top_left", string flipY = "asset_defined",
        string wrapS = "repeat", string wrapT = "repeat")
    {
        var method = typeof(ReferenceModelLoader).GetMethod("SampleTexture",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var parameters = new object[] { image, u, v, null!, null!, null!, null!, uvOrigin, flipY, wrapS, wrapT };
        method!.Invoke(null, parameters);
        r = (byte)parameters[3];
        g = (byte)parameters[4];
        b = (byte)parameters[5];
        a = (byte)parameters[6];
    }

    private static ReferenceVertex[] InvokeBakeVertexColors(
        ReferenceVertex[] vertices, ImageResult diffuse, ImageResult? emissive, float emissiveBrightness,
        string uvOrigin = "top_left", string flipY = "asset_defined",
        string wrapS = "repeat", string wrapT = "repeat")
    {
        var loader = CreateLoader();
        var method = typeof(ReferenceModelLoader).GetMethod("BakeVertexColors",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method!.Invoke(loader, new object?[] { vertices, diffuse, emissive, emissiveBrightness, uvOrigin, flipY, wrapS, wrapT });
        return (ReferenceVertex[])result!;
    }

    private static ReferenceVertex[] InvokeBlendEmissive(
        ReferenceVertex[] vertices, ImageResult emissive, float brightness,
        string uvOrigin = "top_left", string flipY = "asset_defined",
        string wrapS = "repeat", string wrapT = "repeat")
    {
        var loader = CreateLoader();
        var method = typeof(ReferenceModelLoader).GetMethod("BlendEmissive",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = method!.Invoke(loader, new object?[] { vertices, emissive, brightness, uvOrigin, flipY, wrapS, wrapT });
        return (ReferenceVertex[])result!;
    }
}
