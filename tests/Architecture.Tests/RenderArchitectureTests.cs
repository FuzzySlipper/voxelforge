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

    // ── Viewer HTML must not have substantial inline executable JS ──

    [Fact]
    public void ViewerHtml_HasNoSubstantialInlineJavaScript()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var viewerPath = Path.Combine(root, "src", "VoxelForge.Mcp", "wwwroot", "viewer.html");

        Assert.True(File.Exists(viewerPath), $"viewer.html not found at {viewerPath}");

        var text = File.ReadAllText(viewerPath);
        var lines = text.Split('\n');

        var scriptTagCount = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("<script", StringComparison.OrdinalIgnoreCase))
            {
                if (!trimmed.Contains("src=\"") && !trimmed.Contains("src='"))
                {
                    Assert.Fail($"viewer.html contains inline script tag without src attribute.");
                }
                scriptTagCount++;
            }
        }

        Assert.True(scriptTagCount <= 1,
            $"viewer.html has {scriptTagCount} <script> tags; expected at most 1 (module loader)");

        Assert.DoesNotContain("<script>", text, StringComparison.OrdinalIgnoreCase);
    }

    // ── renderer-core exists and exports expected modules ──

    [Fact]
    public void RendererCore_Modules_Exist()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var rendererCorePath = Path.Combine(root, "electron", "src", "renderer-core");

        Assert.True(Directory.Exists(rendererCorePath), "renderer-core directory not found");

        var expectedFiles = new[]
        {
            Path.Combine(rendererCorePath, "index.ts"),
            Path.Combine(rendererCorePath, "protocol", "types.ts"),
            Path.Combine(rendererCorePath, "protocol", "normalizeSnapshot.ts"),
            Path.Combine(rendererCorePath, "scene", "VoxelForgeScene.ts"),
            Path.Combine(rendererCorePath, "scene", "referenceModels.ts"),
            Path.Combine(rendererCorePath, "scene", "materials.ts"),
            Path.Combine(rendererCorePath, "scene", "captureReady.ts"),
            Path.Combine(rendererCorePath, "transport", "RenderProtocolClient.ts"),
            Path.Combine(rendererCorePath, "transport", "HttpSseRenderClient.ts"),
            Path.Combine(rendererCorePath, "transport", "DenBridgeRenderClient.ts"),
        };

        foreach (var file in expectedFiles)
        {
            Assert.True(File.Exists(file), $"Missing renderer-core file: {file}");
        }
    }

    [Fact]
    public void McpViewerEntryPoint_Exists()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var mcpViewerEntry = Path.Combine(root, "electron", "src", "mcp-viewer", "main.ts");
        Assert.True(File.Exists(mcpViewerEntry), "MCP viewer entrypoint not found at electron/src/mcp-viewer/main.ts");
    }

    // ── Cleanup/parity doc (#1659) ──

    [Fact]
    public void CleanupParityDoc_Exists()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        Assert.True(File.Exists(docPath), "Cleanup/parity doc not found at docs/architecture/renderer-cleanup-parity-ruleweaver.md");
    }

    [Fact]
    public void CleanupDoc_DescribesGreenPath()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        var text = File.ReadAllText(docPath);
        Assert.Contains("api/render/state", text);
        Assert.Contains("api/render/snapshot", text);
        Assert.Contains("renderer-core", text);
        Assert.Contains("VoxelForgeWorkspaceState", text);
        Assert.Contains("RenderSceneSnapshotService", text);
    }

    [Fact]
    public void CleanupDoc_DocumentsTransitionalEndpoints()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        var text = File.ReadAllText(docPath);
        Assert.Contains("/api/viewer-state", text);
        Assert.Contains("/api/mesh-snapshot", text);
        Assert.Contains("/api/palette", text);
    }

    [Fact]
    public void CleanupDoc_ContainsRuleWeaverTransferPattern()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        var text = File.ReadAllText(docPath);
        Assert.Contains("RuleWeaver Transfer Pattern", text);
        Assert.Contains("Green path:", text);
        Assert.Contains("Known Integrity Constraints", text);
    }

    // ── Canonical endpoint documentation in mcp-server docs ──

    [Fact]
    public void McpServerDoc_ListsCanonicalEndpoints()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "mcp-server.md");
        Assert.True(File.Exists(docPath), "MCP server doc not found at docs/mcp-server.md");
        var text = File.ReadAllText(docPath);
        Assert.Contains("/api/render/state", text);
        Assert.Contains("/api/render/snapshot", text);
        Assert.Contains("Canonical", text);
    }

    [Fact]
    public void McpServerDoc_DocumentsDeprecatedEndpoints()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "mcp-server.md");
        var text = File.ReadAllText(docPath);
        Assert.Contains("Deprecated transitional endpoints", text);
        Assert.Contains("/api/viewer-state", text);
        Assert.Contains("/api/mesh-snapshot", text);
        Assert.Contains("/api/palette", text);
    }

    // ── Transitional endpoints have #1659 deprecation comments in source ──

    [Fact]
    public void TransitionalEndpoints_HaveDeprecationComments()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var viewerEndpoints = Path.Combine(root, "src", "VoxelForge.Mcp", "Viewer", "ViewerEndpoints.cs");
        var text = File.ReadAllText(viewerEndpoints);

        // The transitional endpoints should have explicit @deprecated comments
        Assert.Contains("@deprecated TRANSITIONAL alias for /api/render/state", text);
        Assert.Contains("@deprecated TRANSITIONAL alias for /api/render/snapshot", text);
        Assert.Contains("@deprecated Palette data is now included in /api/render/state", text);
    }

    // ── Green path: canonical endpoints remain ──

    [Fact]
    public void CanonicalRenderEndpoints_Exist()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var viewerEndpoints = Path.Combine(root, "src", "VoxelForge.Mcp", "Viewer", "ViewerEndpoints.cs");
        var text = File.ReadAllText(viewerEndpoints);

        Assert.Contains("api/render/state", text);
        Assert.Contains("api/render/snapshot", text);
        Assert.Contains("api/viewer-events", text);
        Assert.Contains("api/reference-texture", text);
        Assert.Contains("viewer", text);
    }

    // ── Bridge command/channel registration parity (Task #1665) ──

    [Fact]
    public void Preload_Allows_CanonicalRenderChannels()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var preloadPath = Path.Combine(root, "electron", "src", "preload", "index.ts");
        var text = File.ReadAllText(preloadPath);

        // The preload must allow the canonical render-snapshot and render-state channels
        Assert.Contains("bridge:render-snapshot", text);
        Assert.Contains("bridge:render-state", text);

        // The preload must allow the render control commands
        // Note: bridge:set-grid-visible, bridge:set-wireframe,
        // bridge:set-background-color, and bridge:capture-screenshot were
        // removed as dead channels — they had no corresponding ipcMain.handle
        // in the main process. The preload only exposes channels that are
        // actually wired in the main process.

        // The preload must allow bridge event channels
        Assert.Contains("voxelforge:mesh-update", text);
        Assert.Contains("voxelforge:state-delta", text);
    }

    [Fact]
    public void Preload_Rejects_UncategorizedChannels()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var preloadPath = Path.Combine(root, "electron", "src", "preload", "index.ts");
        var text = File.ReadAllText(preloadPath);

        // The preload must validate channels — look for validateChannel function
        Assert.Contains("validateChannel", text);
        Assert.Contains("allowedChannels", text);
        Assert.Contains("allowedEventChannels", text);
    }

    [Fact]
    public void BridgeProgram_Registers_CanonicalCommands()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var bridgePath = Path.Combine(root, "src", "VoxelForge.Bridge", "Program.cs");
        var text = File.ReadAllText(bridgePath);

        // Must register the mesh.request_snapshot command used by render-snapshot
        Assert.Contains("voxelforge.mesh.request_snapshot", text);
        Assert.Contains("voxelforge.palette.get", text);
        Assert.Contains("voxelforge.state.subscribe", text);
        Assert.Contains("voxelforge.state.request_full", text);
        Assert.Contains("voxelforge.command.execute", text);
        Assert.Contains("voxelforge.history.undo", text);
        Assert.Contains("voxelforge.history.redo", text);
        Assert.Contains("voxelforge.project.save", text);
        Assert.Contains("voxelforge.project.load", text);

        // Must register event types
        Assert.Contains("voxelforge.mesh.update", text);
        Assert.Contains("voxelforge.palette.update", text);
        Assert.Contains("voxelforge.state.delta", text);
    }

    [Fact]
    public void BridgeIpc_Channels_Match_PreloadChannels()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var mainPath = Path.Combine(root, "electron", "src", "main", "index.ts");
        var text = File.ReadAllText(mainPath);

        // The main process must handle the canonical channels that the preload allows
        Assert.Contains("bridge:handshake", text);
        Assert.Contains("bridge:mesh-snapshot", text);
        Assert.Contains("bridge:render-snapshot", text);
        Assert.Contains("bridge:render-state", text);

        // The main process must map to the correct bridge commands
        Assert.Contains("voxelforge.mesh.request_snapshot", text);
    }

    // ── Doc accuracy: no obsolete CDN/polling references (Task #1665) ──

    [Fact]
    public void McpServerDoc_DoesNotMention_CdnLoading()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "mcp-server.md");
        var text = File.ReadAllText(docPath);

        // Must not describe CDN-based loading of dependencies — Three.js and OrbitControls
        // are bundled in viewer-bundle.js.  Allow truthful negations like "no CDN dependencies".
        var lines = text.Split('\n');
        bool hasCdnLoadingClaim = false;
        foreach (var line in lines)
        {
            // Flag lines that mention CDN as a positive loading source, not negated claims
            if (line.Contains("CDN", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("no CDN", StringComparison.OrdinalIgnoreCase))
            {
                hasCdnLoadingClaim = true;
                break;
            }
        }
        Assert.False(hasCdnLoadingClaim,
            "mcp-server.md still describes loading dependencies from a CDN (Three.js/OrbitControls are bundled in viewer-bundle.js).");
    }

    [Fact]
    public void McpServerDoc_DoesNotDescribe_OldPollingBehavior()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "mcp-server.md");
        var text = File.ReadAllText(docPath);

        // Must not describe any 2-second polling interval as current behavior,
        // regardless of endpoint (old /api/mesh-snapshot or stale /api/render/state).
        // SSE is primary; polling is a fallback with a 3-second interval.
        var lines = text.Split('\n');
        bool hasStalePollingClaim = false;
        foreach (var line in lines)
        {
            // Check for lines about polling any endpoint on a 2-second fixed interval
            if (line.Contains("2 second") &&
                (line.Contains("mesh-snapshot") || line.Contains("render/state") || line.Contains("render-state")))
            {
                hasStalePollingClaim = true;
                break;
            }
        }
        Assert.False(hasStalePollingClaim,
            "mcp-server.md still describes a stale 2-second polling interval (old /api/mesh-snapshot or stale /api/render/state) as current behavior.");
    }

    [Fact]
    public void McpServerDoc_Describes_SseAsPrimaryTransport()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "mcp-server.md");
        var text = File.ReadAllText(docPath);

        // Must describe SSE as the primary live-update mechanism
        Assert.Contains("SSE", text);
        Assert.Contains("/api/viewer-events", text);
    }

    // ── Cleanup doc has follow-up task links (Task #1665) ──

    [Fact]
    public void CleanupDoc_HasFollowUpTaskLinks()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        var text = File.ReadAllText(docPath);

        // Must reference follow-up tasks for remaining gaps
        Assert.Contains("follow-up", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CleanupDoc_DoesNotOverclaim_BridgeParity()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "renderer-cleanup-parity-ruleweaver.md");
        var text = File.ReadAllText(docPath);

        // The cleanup doc must not claim full Electron/bridge render-scene/material parity.
        // The MeshSnapshotHandler produces MeshSnapshotResponse (not RenderSceneSnapshot),
        // so DenBridgeRenderClient always falls back to transitional paths for full snapshots.
        // Look for honest gap descriptions rather than overclaimed parity checkmarks.
        int checkCount = text.Split("\u2705").Length - 1;
        Assert.True(checkCount <= 8,
            $"Cleanup doc has {checkCount} parity checkmarks; expected at most 8 given known bridge gaps.");
    }
}
