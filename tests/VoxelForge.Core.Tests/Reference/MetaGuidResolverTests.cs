using VoxelForge.Core.Reference;

namespace VoxelForge.Core.Tests.Reference;

public sealed class MetaGuidResolverTests
{
    [Fact]
    public void ExtractGuid_StandardMetaFile_ReturnsGuid()
    {
        var metaContent = """
            fileFormatVersion: 2
            guid: abcdef1234567890abcdef1234567890
            folderAsset: no
            timeCreated: 1234567890
            licenseType: Free
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-metatest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var metaPath = Path.Combine(tempDir, "SomeTexture.png.meta");
            File.WriteAllText(metaPath, metaContent);

            var guid = MetaGuidResolver.ExtractGuid(metaPath);
            Assert.Equal("abcdef1234567890abcdef1234567890", guid);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ExtractGuid_NoGuidLine_ReturnsNull()
    {
        var metaContent = """
            fileFormatVersion: 2
            folderAsset: no
            timeCreated: 1234567890
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-metatest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var metaPath = Path.Combine(tempDir, "SomeTexture.png.meta");
            File.WriteAllText(metaPath, metaContent);

            var guid = MetaGuidResolver.ExtractGuid(metaPath);
            Assert.Null(guid);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildGuidMap_ScansMetaFiles_CreatesMap()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-metatest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create texture with meta
            var texPath = Path.Combine(tempDir, "albedo.png");
            File.WriteAllText(texPath, "fake image bytes");
            File.WriteAllText(texPath + ".meta", $@"
fileFormatVersion: 2
guid: aaaaaaaabbbbbbbbccccccccdddddddd
timeCreated: 1234567890
");

            // Create subdirectory texture
            var subDir = Path.Combine(tempDir, "Textures");
            Directory.CreateDirectory(subDir);
            var subTexPath = Path.Combine(subDir, "diffuse.jpg");
            File.WriteAllText(subTexPath, "fake image bytes");
            File.WriteAllText(subTexPath + ".meta", $@"
fileFormatVersion: 2
guid: eeeeeeeeffffffff1111111122222222
timeCreated: 1234567890
");

            // Non-image .meta should be skipped
            var scriptPath = Path.Combine(tempDir, "MyScript.cs");
            File.WriteAllText(scriptPath, "// fake");
            File.WriteAllText(scriptPath + ".meta", $@"
fileFormatVersion: 2
guid: 33333333444444445555555566666666
timeCreated: 1234567890
");

            var guidMap = MetaGuidResolver.BuildGuidMap([tempDir]);

            Assert.Equal(2, guidMap.Count);
            Assert.True(guidMap.ContainsKey("aaaaaaaabbbbbbbbccccccccdddddddd"));
            Assert.True(guidMap.ContainsKey("eeeeeeeeffffffff1111111122222222"));
            Assert.Equal(Path.GetFullPath(texPath), guidMap["aaaaaaaabbbbbbbbccccccccdddddddd"]);
            Assert.Equal(Path.GetFullPath(subTexPath), guidMap["eeeeeeeeffffffff1111111122222222"]);
            Assert.False(guidMap.ContainsKey("33333333444444445555555566666666"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveGuid_FindsTextureInSubdirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-metatest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var subDir = Path.Combine(tempDir, "Materials", "Textures");
            Directory.CreateDirectory(subDir);

            var texPath = Path.Combine(subDir, "my_texture.tga");
            File.WriteAllText(texPath, "fake tga");
            File.WriteAllText(texPath + ".meta", $@"
fileFormatVersion: 2
guid: abcdef0123456789abcdef0123456789
timeCreated: 1234567890
");

            var resolved = MetaGuidResolver.ResolveGuid("abcdef0123456789abcdef0123456789", [tempDir]);
            Assert.NotNull(resolved);
            Assert.Equal(Path.GetFullPath(texPath), resolved);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveGuid_NonExistentGuid_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "voxelforge-metatest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var resolved = MetaGuidResolver.ResolveGuid("nonexistentguid1234567890abcdef", [tempDir]);
            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void BuildGuidMap_NonExistentDirectory_ReturnsEmptyMap()
    {
        var guidMap = MetaGuidResolver.BuildGuidMap(["/nonexistent/path/that/does/not/exist"]);
        Assert.Empty(guidMap);
    }
}
