using System.Reflection;

namespace Architecture.Tests;

public sealed class BoundaryTests
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

    // --- Core boundary: no engine, no UI, no LLM SDK, no App, no Engine types ---

    [Fact]
    public void Core_MustNotReference_MonoGame()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var corePath = Path.Combine(root, "src", "VoxelForge.Core");
        AssertNoUsings(corePath, "VoxelForge.Core", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.App",
            "using VoxelForge.Engine",
            "using VoxelForge.LLM",
            "using VoxelForge.Content",
        ]);
    }

    [Fact]
    public void Core_Assembly_MustNotReference_EngineAssemblies()
    {
        var coreAsm = typeof(VoxelForge.Core.Point3).Assembly;
        var refs = coreAsm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();

        Assert.DoesNotContain(refs, r => r.Contains("MonoGame", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("Myra", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.Engine", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.LLM", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("OpenAI", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("Anthropic", StringComparison.OrdinalIgnoreCase));
    }

    // --- Content boundary: references only Core ---

    [Fact]
    public void Content_MustNotReference_AppOrEngine()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var contentPath = Path.Combine(root, "src", "VoxelForge.Content");
        AssertNoUsings(contentPath, "VoxelForge.Content", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.App",
            "using VoxelForge.Engine",
            "using VoxelForge.LLM",
        ]);
    }

    // --- LLM boundary: references only Core ---

    [Fact]
    public void LLM_MustNotReference_AppOrEngine()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var llmPath = Path.Combine(root, "src", "VoxelForge.LLM");
        AssertNoUsings(llmPath, "VoxelForge.LLM", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.App",
            "using VoxelForge.Engine",
        ]);
    }

    // --- App boundary: must not reference Engine ---

    [Fact]
    public void App_MustNotReference_Engine()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var appPath = Path.Combine(root, "src", "VoxelForge.App");
        AssertNoUsings(appPath, "VoxelForge.App", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.Engine",
        ]);
    }
}
