using System.Reflection;

namespace Architecture.Tests;

/// <summary>
/// Architecture boundary tests for the shared render services added in #1657.
/// Verifies that MCP and Bridge do not reference each other, shared render/session
/// code lives in App/shared layer, and App render services do not depend on
/// UI/adapter/web/den-bridge types.
/// </summary>
public sealed class RenderArchitectureTests
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

    // ── Boundary: MCP must not reference Bridge ──

    [Fact]
    public void Mcp_MustNotReference_Bridge()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var mcpPath = Path.Combine(root, "src", "VoxelForge.Mcp");
        AssertNoUsings(mcpPath, "VoxelForge.Mcp", [
            "using VoxelForge.Bridge",
        ]);
    }

    [Fact]
    public void Mcp_Assembly_MustNotReference_BridgeAssembly()
    {
        var mcpAsm = typeof(VoxelForge.Mcp.VoxelForgeMcpOptions).Assembly;
        var refs = mcpAsm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();

        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.Bridge", StringComparison.OrdinalIgnoreCase));
    }

    // ── Boundary: Bridge must not reference MCP ──

    [Fact]
    public void Bridge_MustNotReference_Mcp()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var bridgePath = Path.Combine(root, "src", "VoxelForge.Bridge");
        AssertNoUsings(bridgePath, "VoxelForge.Bridge", [
            "using VoxelForge.Mcp",
        ]);
    }

    [Fact]
    public void Bridge_Assembly_MustNotReference_McpAssembly()
    {
        var bridgeAsm = typeof(VoxelForge.Bridge.Program).Assembly;
        var refs = bridgeAsm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();

        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.Mcp", StringComparison.OrdinalIgnoreCase));
    }

    // ── Boundary: App must not reference MCP, Bridge, or den-bridge types ──

    [Fact]
    public void App_RenderServices_MustNotReference_TransportOrAdapterTypes()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var appPath = Path.Combine(root, "src", "VoxelForge.App");
        AssertNoUsings(appPath, "VoxelForge.App", [
            "using VoxelForge.Bridge",
            "using VoxelForge.Mcp",
            "using Den.Bridge",
            "using Microsoft.AspNetCore",
            "using ModelContextProtocol",
        ]);
    }

    [Fact]
    public void App_Assembly_MustNotReference_TransportOrAdapterAssemblies()
    {
        var appAsm = typeof(VoxelForge.App.EditorDocumentState).Assembly;
        var refs = appAsm.GetReferencedAssemblies().Select(r => r.Name ?? "").ToList();

        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.Bridge", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("VoxelForge.Mcp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("Den.Bridge", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(refs, r => r.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    // ── Boundary: App must not reference Engine types ──

    [Fact]
    public void App_RenderServices_MustNotReference_EngineOrRendering()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var appPath = Path.Combine(root, "src", "VoxelForge.App");
        AssertNoUsings(appPath, "VoxelForge.App", [
            "using Microsoft.Xna.Framework",
            "using Myra",
            "using VoxelForge.Engine",
        ]);
    }

    // ── Boundary: Render types must be in App namespace ──

    [Fact]
    public void RenderSceneSnapshot_Types_Reside_In_App_Namespace()
    {
        var renderType = typeof(VoxelForge.App.Render.RenderSceneSnapshot);
        Assert.Equal("VoxelForge.App.Render", renderType.Namespace);
    }

    [Fact]
    public void RenderSceneSnapshot_Contains_ExpectedFields()
    {
        var snapshotType = typeof(VoxelForge.App.Render.RenderSceneSnapshot);

        Assert.NotNull(snapshotType.GetProperty("SchemaVersion"));
        Assert.NotNull(snapshotType.GetProperty("Revision"));
        Assert.NotNull(snapshotType.GetProperty("ModelId"));
        Assert.NotNull(snapshotType.GetProperty("Source"));
        Assert.NotNull(snapshotType.GetProperty("Bounds"));
        Assert.NotNull(snapshotType.GetProperty("ReferenceBounds"));
        Assert.NotNull(snapshotType.GetProperty("CombinedBounds"));
        Assert.NotNull(snapshotType.GetProperty("VoxelMeshes"));
        Assert.NotNull(snapshotType.GetProperty("ReferenceNodes"));
        Assert.NotNull(snapshotType.GetProperty("Materials"));
        Assert.NotNull(snapshotType.GetProperty("Textures"));
        Assert.NotNull(snapshotType.GetProperty("Palette"));
        Assert.NotNull(snapshotType.GetProperty("Diagnostics"));
    }

    // ── Boundary: VoxelForgeWorkspaceState is in App namespace ──

    [Fact]
    public void VoxelForgeWorkspaceState_Resides_In_App_Workspaces_Namespace()
    {
        var stateType = typeof(VoxelForge.App.Workspaces.VoxelForgeWorkspaceState);
        Assert.Equal("VoxelForge.App.Workspaces", stateType.Namespace);
    }

    [Fact]
    public void WorkspaceState_Contains_ExpectedProperties()
    {
        var stateType = typeof(VoxelForge.App.Workspaces.VoxelForgeWorkspaceState);

        Assert.NotNull(stateType.GetProperty("Document"));
        Assert.NotNull(stateType.GetProperty("Session"));
        Assert.NotNull(stateType.GetProperty("UndoHistory"));
        Assert.NotNull(stateType.GetProperty("UndoStack"));
        Assert.NotNull(stateType.GetProperty("Events"));
        Assert.NotNull(stateType.GetProperty("ReferenceModels"));
        Assert.NotNull(stateType.GetProperty("ReferenceImages"));
        Assert.NotNull(stateType.GetProperty("ModelId"));
        Assert.NotNull(stateType.GetProperty("ProjectPath"));
        Assert.NotNull(stateType.GetProperty("CurrentModelName"));
        Assert.NotNull(stateType.GetProperty("IsDirty"));
        Assert.NotNull(stateType.GetProperty("StatusMessage"));
        Assert.NotNull(stateType.GetProperty("Revision"));
    }

    // ── Render services exist and are in App.Services namespace ──

    [Fact]
    public void RenderSceneSnapshotService_Exists_In_App_Services()
    {
        var serviceType = typeof(VoxelForge.App.Services.RenderSceneSnapshotService);
        Assert.Equal("VoxelForge.App.Services", serviceType.Namespace);

        Assert.NotNull(serviceType.GetMethod("BuildSnapshot"));
        Assert.NotNull(serviceType.GetMethod("BuildState"));
    }

    [Fact]
    public void RenderSceneEventProjector_Exists_In_App_Services()
    {
        var projectorType = typeof(VoxelForge.App.Services.RenderSceneEventProjector);
        Assert.Equal("VoxelForge.App.Services", projectorType.Namespace);

        Assert.NotNull(projectorType.GetMethod("Project"));
    }

    [Fact]
    public void WorkspaceCommandApplicationService_Exists_In_App_Services()
    {
        var serviceType = typeof(VoxelForge.App.Services.WorkspaceCommandApplicationService);
        Assert.Equal("VoxelForge.App.Services", serviceType.Namespace);

        Assert.NotNull(serviceType.GetMethod("Execute"));
        Assert.NotNull(serviceType.GetMethod("Undo"));
        Assert.NotNull(serviceType.GetMethod("Redo"));
    }

    [Fact]
    public void ReferenceModelApplicationService_Exists_In_App_Services()
    {
        var serviceType = typeof(VoxelForge.App.Services.ReferenceModelApplicationService);
        Assert.Equal("VoxelForge.App.Services", serviceType.Namespace);

        Assert.NotNull(serviceType.GetMethod("RemoveReferenceModel"));
        Assert.NotNull(serviceType.GetMethod("ClearReferenceModels"));
        Assert.NotNull(serviceType.GetMethod("IsTextureAuthorized"));
    }

    // ── No new inline JavaScript ──

    [Fact]
    public void NoNewInlineJavaScript_Introduced()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var mcpPath = Path.Combine(root, "src", "VoxelForge.Mcp");

        var csFiles = Directory.EnumerateFiles(mcpPath, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

        var inlineJsFiles = new List<string>();
        foreach (var file in csFiles)
        {
            var text = File.ReadAllText(file);
            // Flag files with raw <script> tags or string constants containing JS code
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Contains("<script>", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("\"<script", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("'<script", StringComparison.OrdinalIgnoreCase))
                {
                    inlineJsFiles.Add($"{Path.GetRelativePath(mcpPath, file)}:{i + 1}: {line[..Math.Min(line.Length, 80)]}");
                }
            }
        }

        Assert.True(inlineJsFiles.Count == 0,
            "New inline executable JavaScript detected:\n" + string.Join(Environment.NewLine, inlineJsFiles));
    }
}
