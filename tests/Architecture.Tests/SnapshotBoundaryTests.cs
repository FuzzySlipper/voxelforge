using System.Reflection;

namespace Architecture.Tests;

public sealed class SnapshotBoundaryTests
{
    private static string FindRepoRoot(string startPath)
    {
        var dir = new DirectoryInfo(startPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "voxelforge.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate voxelforge repository root.");
    }

    private static void AssertNoUsings(string projectDir, string projectName, string[] bannedUsings)
    {
        Assert.True(Directory.Exists(projectDir), $"{projectName} directory not found at {projectDir}");

        var csFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var violations = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var banned in bannedUsings)
            {
                if (text.Contains(banned, StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(projectDir, file)}: contains '{banned}'");
            }
        }

        Assert.True(violations.Count == 0,
            $"{projectName} has forbidden references:\n{string.Join(Environment.NewLine, violations)}");
    }

    /// <summary>
    /// Snapshot DTOs in VoxelForge.App/Snapshots must not reference any rendering
    /// framework types (FNA, Myra, Engine). They must be renderer-neutral.
    /// </summary>
    [Fact]
    public void Snapshot_Namespace_MustNotReference_EngineOrRendering()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var snapshotPath = Path.Combine(root, "src", "VoxelForge.App", "Snapshots");
        if (!Directory.Exists(snapshotPath))
            return; // Snapshot directory doesn't exist yet; skip

        AssertNoUsings(snapshotPath, "VoxelForge.App/Snapshots", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.Engine",
        ]);
    }

    /// <summary>
    /// MeshSnapshotService must not reference Engine types — it produces
    /// renderer-neutral data from Core meshing only.
    /// </summary>
    [Fact]
    public void MeshSnapshotService_MustNotReference_EngineOrRendering()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var appPath = Path.Combine(root, "src", "VoxelForge.App");
        if (!File.Exists(Path.Combine(appPath, "Services", "MeshSnapshotService.cs")))
            return; // File doesn't exist yet; skip

        var serviceText = File.ReadAllText(Path.Combine(appPath, "Services", "MeshSnapshotService.cs"));
        Assert.False(serviceText.Contains("using Microsoft.Xna.Framework", StringComparison.Ordinal),
            "MeshSnapshotService must not reference FNA types");
        Assert.False(serviceText.Contains("using Myra", StringComparison.Ordinal),
            "MeshSnapshotService must not reference Myra types");
        Assert.False(serviceText.Contains("using VoxelForge.Engine", StringComparison.Ordinal),
            "MeshSnapshotService must not reference Engine types");
    }

    /// <summary>
    /// Snapshot service assembly boundary: App assembly must not reference Engine assemblies.
    /// This is already checked by BoundaryTests, but we validate the snapshot-specific
    /// types are in the correct project.
    /// </summary>
    [Fact]
    public void Snapshot_Types_Reside_In_App_Project()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var snapshotPath = Path.Combine(root, "src", "VoxelForge.App", "Snapshots");

        if (!Directory.Exists(snapshotPath))
            return; // No snapshot directory yet

        var snapshotFiles = Directory.GetFiles(snapshotPath, "*.cs");
        Assert.True(snapshotFiles.Length > 0,
            "Expected at least one snapshot DTO file in VoxelForge.App/Snapshots");

        foreach (var file in snapshotFiles)
        {
            var text = File.ReadAllText(file);
            Assert.True(text.Contains("namespace VoxelForge.App.Snapshots", StringComparison.Ordinal),
                $"Snapshot file {Path.GetFileName(file)} should be in the VoxelForge.App.Snapshots namespace");
        }
    }

    /// <summary>
    /// No static singletons or service locator patterns in snapshot types.
    /// </summary>
    [Fact]
    public void Snapshot_Types_MustNotUse_StaticSingletons()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var snapshotPath = Path.Combine(root, "src", "VoxelForge.App", "Snapshots");

        if (!Directory.Exists(snapshotPath))
            return;

        var snapshotFiles = Directory.GetFiles(snapshotPath, "*.cs");
        foreach (var file in snapshotFiles)
        {
            var text = File.ReadAllText(file);
            Assert.False(text.Contains("Instance", StringComparison.Ordinal),
                $"Snapshot file {Path.GetFileName(file)} must not use Instance singleton patterns");
            Assert.False(text.Contains("static readonly", StringComparison.Ordinal),
                $"Snapshot file {Path.GetFileName(file)} must not use static readonly fields (no static singletons)");
        }
    }
}