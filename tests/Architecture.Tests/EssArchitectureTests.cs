using System.Reflection;
using VoxelForge.App.Events;
using VoxelForge.App.Services;
using VoxelForge.Core.LLM;
using VoxelForge.Core.Services;

namespace Architecture.Tests;

public sealed class EssArchitectureTests
{
    [Fact]
    public void AppServices_DoNotCaptureStateOrAdapterDependencies()
    {
        var serviceTypes = GetAppServiceTypes();
        var violations = new List<string>();

        foreach (var serviceType in serviceTypes)
        {
            foreach (var field in serviceType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsInitOnly)
                    violations.Add($"{serviceType.Name}.{field.Name}: service instance fields must be readonly dependencies");

                AddForbiddenApiViolation(violations, serviceType, $"field {field.Name}", field.FieldType);
                if (IsStateType(field.FieldType))
                    violations.Add($"{serviceType.Name}.{field.Name}: services must not store *State instances");
            }

            foreach (var constructor in serviceType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    AddForbiddenApiViolation(violations, serviceType, $"constructor parameter {parameter.Name}", parameter.ParameterType);
                    if (IsStateType(parameter.ParameterType))
                        violations.Add($"{serviceType.Name} constructor parameter {parameter.Name}: pass state to operations, not service constructors");
                }
            }

            foreach (var method in serviceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName)
                    continue;

                AddForbiddenApiViolation(violations, serviceType, $"method {method.Name} return", method.ReturnType);
                foreach (var parameter in method.GetParameters())
                    AddForbiddenApiViolation(violations, serviceType, $"method {method.Name} parameter {parameter.Name}", parameter.ParameterType);
            }
        }

        Assert.True(violations.Count == 0,
            "ESS service boundary violations:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AppServiceResults_CarryTypedApplicationEvents()
    {
        var eventsProperty = typeof(ApplicationServiceResult).GetProperty(nameof(ApplicationServiceResult.Events));

        Assert.NotNull(eventsProperty);
        Assert.Equal(typeof(IReadOnlyList<IApplicationEvent>), eventsProperty.PropertyType);
    }

    [Fact]
    public void ApplicationEvents_AreTypedFacts()
    {
        var eventTypes = typeof(IApplicationEvent).Assembly.GetTypes()
            .Where(t => typeof(IApplicationEvent).IsAssignableFrom(t)
                     && t.IsClass
                     && !t.IsAbstract)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(eventTypes);

        var violations = new List<string>();
        foreach (var eventType in eventTypes)
        {
            if (!eventType.Name.EndsWith("Event", StringComparison.Ordinal))
                violations.Add($"{eventType.FullName}: application event implementations must use *Event names");

            if (typeof(Delegate).IsAssignableFrom(eventType))
                violations.Add($"{eventType.FullName}: application events must be data facts, not delegates");
        }

        Assert.True(violations.Count == 0,
            "Application event naming violations:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void LlmToolHandlers_ReturnTypedMutationIntents_NotDeferredActions()
    {
        var mutationIntentProperty = typeof(ToolHandlerResult).GetProperty(nameof(ToolHandlerResult.MutationIntent));
        Assert.NotNull(mutationIntentProperty);
        Assert.Equal(typeof(VoxelMutationIntent), mutationIntentProperty.PropertyType);

        var delegateProperties = typeof(ToolHandlerResult).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => typeof(Delegate).IsAssignableFrom(UnwrapType(p.PropertyType)))
            .Select(p => p.Name)
            .ToArray();

        Assert.Empty(delegateProperties);

        var root = FindRepoRoot(AppContext.BaseDirectory);
        var sourceRoot = Path.Combine(root, "src");
        AssertNoSourceOccurrences(sourceRoot, "ApplyAction", "LLM tools must return typed mutation intents, not deferred mutation delegates.");
    }

    [Fact]
    public void EssArchitectureDocument_DefinesMcpAdapterSeam()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "events-states-services.md");

        Assert.True(File.Exists(docPath), $"Missing ESS architecture document at {docPath}");

        var text = File.ReadAllText(docPath);
        Assert.Contains("MCP", text, StringComparison.Ordinal);
        Assert.Contains("thin adapter", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typed service", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IApplicationEvent", text, StringComparison.Ordinal);
        Assert.Contains("VoxelMutationIntent", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmPrimitiveGenerationDesign_DefinesToolSurfaceAndUndoPath()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "llm-primitive-generation-surface.md");

        Assert.True(File.Exists(docPath), $"Missing LLM primitive generation design document at {docPath}");

        var text = File.ReadAllText(docPath);
        Assert.Contains("apply_voxel_primitives", text, StringComparison.Ordinal);
        Assert.Contains("block", text, StringComparison.Ordinal);
        Assert.Contains("box", text, StringComparison.Ordinal);
        Assert.Contains("line", text, StringComparison.Ordinal);
        Assert.Contains("VoxelPrimitiveGenerationService", text, StringComparison.Ordinal);
        Assert.Contains("VoxelMutationIntent", text, StringComparison.Ordinal);
        Assert.Contains("LlmToolApplicationService", text, StringComparison.Ordinal);
        Assert.Contains("VoxelEditingService.ApplyMutationIntent", text, StringComparison.Ordinal);
        Assert.Contains("UndoStack", text, StringComparison.Ordinal);
        Assert.Contains("CompoundCommand", text, StringComparison.Ordinal);
        Assert.Contains("SetVoxelCommand", text, StringComparison.Ordinal);
        Assert.Contains("LlmToolMcpTool", text, StringComparison.Ordinal);
        Assert.Contains("Engine", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmHeadlessBenchmarkDesign_DefinesArtifactsAndComparisonScope()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var docPath = Path.Combine(root, "docs", "architecture", "llm-headless-benchmark-harness.md");

        Assert.True(File.Exists(docPath), $"Missing LLM benchmark design document at {docPath}");

        var text = File.ReadAllText(docPath);
        Assert.Contains("run-manifest.json", text, StringComparison.Ordinal);
        Assert.Contains("final.vforge", text, StringComparison.Ordinal);
        Assert.Contains("conversation.jsonl", text, StringComparison.Ordinal);
        Assert.Contains("metrics.json", text, StringComparison.Ordinal);
        Assert.Contains("comparison.md", text, StringComparison.Ordinal);
        Assert.Contains("VoxelForge.Mcp", text, StringComparison.Ordinal);
        Assert.Contains("StdioHost", text, StringComparison.Ordinal);
        Assert.Contains("ToolLoop", text, StringComparison.Ordinal);
        Assert.Contains("LlmToolApplicationService", text, StringComparison.Ordinal);
        Assert.Contains("screenshots are explicitly not part of the required first version", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not reference `VoxelForge.Engine.MonoGame`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EssArchitectureDocuments_ReferenceExistingStateAndFacadeTypes()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var architectureDocs = string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "docs", "architecture", "events-states-services.md")),
            File.ReadAllText(Path.Combine(root, "docs", "architecture", "state-boundaries.md")));

        var appAssembly = typeof(VoxelEditingService).Assembly;
        var documentedTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["EditorDocumentState"] = "VoxelForge.App.EditorDocumentState",
            ["EditorSessionState"] = "VoxelForge.App.EditorSessionState",
            ["EditorConfigState"] = "VoxelForge.App.EditorConfigState",
            ["UndoHistoryState"] = "VoxelForge.App.Commands.UndoHistoryState",
            ["ReferenceModelState"] = "VoxelForge.App.Reference.ReferenceModelState",
            ["ReferenceImageState"] = "VoxelForge.App.Reference.ReferenceImageState",
            ["EditorState"] = "VoxelForge.App.EditorState",
            ["UndoStack"] = "VoxelForge.App.Commands.UndoStack",
        };

        var violations = new List<string>();
        foreach (var documentedType in documentedTypes)
        {
            if (!architectureDocs.Contains($"`{documentedType.Key}`", StringComparison.Ordinal))
                violations.Add($"Architecture docs no longer mention `{documentedType.Key}`.");

            var resolvedType = appAssembly.GetType(documentedType.Value, throwOnError: false);
            if (resolvedType is null)
                violations.Add($"Documented type `{documentedType.Key}` no longer resolves as {documentedType.Value}.");
        }

        Assert.True(violations.Count == 0,
            "ESS architecture doc drift detected:\n" + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Source_DoesNotIntroduceStaticSingletonsOrServiceLocators()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var sourceRoot = Path.Combine(root, "src");

        AssertNoSourceOccurrences(sourceRoot, "ServiceLocator", "Dependencies must flow through constructors; ServiceLocator is forbidden.");
        AssertNoPublicStaticInstanceProperties(sourceRoot);
    }

    private static Type[] GetAppServiceTypes()
    {
        return typeof(VoxelEditingService).Assembly.GetTypes()
            .Where(t => t.IsClass
                     && !t.IsAbstract
                     && t.Namespace == "VoxelForge.App.Services"
                     && t.Name.EndsWith("Service", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddForbiddenApiViolation(List<string> violations, Type owner, string memberDescription, Type type)
    {
        foreach (var referencedType in FlattenType(type))
        {
            if (IsForbiddenAdapterType(referencedType))
                violations.Add($"{owner.Name} {memberDescription}: references adapter/UI type {referencedType.FullName}");

            if (typeof(Delegate).IsAssignableFrom(referencedType))
                violations.Add($"{owner.Name} {memberDescription}: services must not expose delegate callback contracts ({referencedType.FullName})");
        }
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        var unwrapped = UnwrapType(type);
        yield return unwrapped;

        if (unwrapped.IsGenericType)
        {
            foreach (var argument in unwrapped.GetGenericArguments())
            {
                foreach (var nested in FlattenType(argument))
                    yield return nested;
            }
        }
    }

    private static Type UnwrapType(Type type)
    {
        while (type.HasElementType)
            type = type.GetElementType() ?? type;

        var underlying = Nullable.GetUnderlyingType(type);
        return underlying ?? type;
    }

    private static bool IsForbiddenAdapterType(Type type)
    {
        var ns = type.Namespace ?? string.Empty;
        return ns.StartsWith("VoxelForge.App.Console", StringComparison.Ordinal)
            || ns.StartsWith("VoxelForge.Engine", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal)
            || ns.StartsWith("Myra", StringComparison.Ordinal);
    }

    private static bool IsStateType(Type type)
    {
        var unwrapped = UnwrapType(type);
        return unwrapped.Name.EndsWith("State", StringComparison.Ordinal);
    }

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

    private static void AssertNoSourceOccurrences(string directory, string forbiddenText, string message)
    {
        var violations = new List<string>();
        foreach (var file in EnumerateSourceFiles(directory))
        {
            var text = File.ReadAllText(file);
            if (text.Contains(forbiddenText, StringComparison.Ordinal))
                violations.Add(Path.GetRelativePath(directory, file));
        }

        Assert.True(violations.Count == 0,
            message + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static void AssertNoPublicStaticInstanceProperties(string directory)
    {
        var violations = new List<string>();
        foreach (var file in EnumerateSourceFiles(directory))
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (line.Contains("public static", StringComparison.Ordinal)
                    && line.Contains(" Instance", StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(directory, file)}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Public static Instance singleton pattern is forbidden in src/.\n" + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);
    }
}
