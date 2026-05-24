using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Core.Services;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Holds the MCP bridge catalog derived from the normal VoxelForge <see cref="CommandRouter"/>.
/// Unknown/denied commands are rejected before reaching the router.
/// Mutating commands require explicit <c>allow_mutation: true</c>.
/// </summary>
public sealed class ConsoleCommandBridgeService
{
    private readonly CommandRouter _router;
    private readonly IReadOnlyDictionary<string, ConsoleCommandBridgeEntry> _entriesByName;
    private readonly IReadOnlyList<ConsoleCommandBridgeEntry> _uniqueEntries;

    public IReadOnlyDictionary<string, ConsoleCommandBridgeEntry> EntriesByName => _entriesByName;

    /// <summary>All unique catalog entries (one per primary command name).</summary>
    public IReadOnlyList<ConsoleCommandBridgeEntry> UniqueEntries => _uniqueEntries;

    /// <summary>
    /// Commands excluded from the bridge catalog. Default is include; exclusions are documented
    /// with exact reasons.
    /// </summary>
    private static readonly HashSet<string> ExcludedCommandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Help lists all router commands, including non-bridge ones (e.g. exec).
        // list_console_commands is the canonical bridge catalog query.
        "help",
        "?",
        "commands",

        // Exec reads arbitrary file paths and chains commands via the internal router,
        // bypassing per-command mutation guards and file-access controls.
        "exec",
        "run",
    };

    public ConsoleCommandBridgeService(CommandRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;

        var dict = new Dictionary<string, ConsoleCommandBridgeEntry>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<ConsoleCommandBridgeEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, cmd) in router.Commands.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (ExcludedCommandNames.Contains(name))
                continue;

            if (!seen.Add(cmd.Name))
                continue;

            var mutates = IsMutatingCommand(cmd.Name);
            var entry = new ConsoleCommandBridgeEntry
            {
                Name = cmd.Name,
                Aliases = cmd.Aliases,
                HelpText = cmd.HelpText,
                MutatesState = mutates,
                BridgeNotes = mutates
                    ? "Mutates model state — requires allow_mutation: true."
                    : null,
            };

            dict[entry.Name] = entry;
            unique.Add(entry);
            foreach (var alias in entry.Aliases)
            {
                if (ExcludedCommandNames.Contains(alias))
                    continue;
                dict.TryAdd(alias, entry);
            }
        }

        _entriesByName = dict;
        _uniqueEntries = unique.AsReadOnly();
    }

    /// <summary>
    /// Execute a bridge-accessible command with tokenized args.
    /// Returns a structured result with metadata about the invocation.
    /// </summary>
    public BridgeExecutionResult Execute(
        string commandName,
        string[] args,
        bool allowMutation,
        CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(commandName);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);

        // 1. Look up command in the bridge catalog (rejects unknown/denied commands).
        if (!_entriesByName.TryGetValue(commandName, out var entry))
        {
            return new BridgeExecutionResult
            {
                Success = false,
                Command = commandName,
                Args = args,
                Message = $"Unknown or denied command: '{commandName}'. Use list_console_commands for available commands.",
                MutatesState = false,
                AllowMutation = allowMutation,
            };
        }

        var activeEntry = entry;

        // 2. Mutation guard: fail closed unless explicitly opted-in.
        if (activeEntry.MutatesState && !allowMutation)
        {
            return new BridgeExecutionResult
            {
                Success = false,
                Command = commandName,
                Args = args,
                Message = $"Command '{commandName}' mutates model state. Set allow_mutation=true to proceed.",
                MutatesState = true,
                AllowMutation = false,
            };
        }

        // 3. Route through CommandRouter — tokenized args, no shell string reconstruction.
        var result = _router.Execute(commandName, args, context);

        return new BridgeExecutionResult
        {
            Success = result.Success,
            Command = commandName,
            Args = args,
            Message = result.Message,
            MutatesState = activeEntry.MutatesState,
            AllowMutation = allowMutation,
        };
    }

    /// <summary>
    /// Comprehensive mutation classification covering the full VoxelForge command surface.
    /// Read-only commands are explicitly listed; everything else defaults to false, but the
    /// switch is exhaustive for clarity and auditability.
    /// </summary>
    private static bool IsMutatingCommand(string name) => name.ToLowerInvariant() switch
    {
        // Voxel edits
        "set" or "s" or "place" => true,
        "remove" or "rm" or "delete" => true,
        "fill" or "f" => true,
        "clear" or "cls" => true,
        "grid" => true,
        "undo" or "u" => true,
        "redo" or "r" => true,
        "voxelize" or "vox" => true,
        "voxcompare" or "vcomp" => true,

        // Region edits
        "label" => true,

        // Palette edits / bakes
        "palette" or "pal" => true,
        "palette-map" or "pmap" => true,
        "palette-reduce" or "preduce" => true,
        "ao-bake" or "ao" => true,
        "edge-darken" or "edged" => true,
        "light-bake" or "lbake" => true,

        // Reference model changes
        "refload" => true,
        "refremove" => true,
        "refclear" => true,
        "reftransform" or "refmove" => true,
        "refmode" => true,
        "refshow" => true,
        "refhide" => true,
        "refscale" => true,
        "refrotate" or "refrot" => true,
        "reforient" or "refautopose" => true,
        "refanim" => true,
        "reftex" => true,
        "reftex-emissive" or "refemissive" => true,
        "refsave" => true,
        "refloadmeta" or "refmeta" => true,

        // Image changes
        "imgload" => true,
        "imgremove" => true,

        // Save / load / config / file-writing / state-changing
        "save" => true,
        "load" => true,
        "config" or "cfg" => true,
        "measure" => true,
        "screenshot" or "ss" => true,

        // Read-only queries
        "describe" or "desc" or "info" => false,
        "get" or "g" => false,
        "getcube" or "gc" => false,
        "getsphere" or "gs" => false,
        "count" => false,
        "list" or "ls" or "files" => false,
        "regions" or "lr" => false,
        "reflist" => false,
        "refinfo" => false,
        "imglist" => false,

        _ => false,
    };
}

/// <summary>
/// Result of a bridge command execution, with metadata.
/// </summary>
public sealed record BridgeExecutionResult
{
    public required bool Success { get; init; }
    public required string Command { get; init; }
    public required string[] Args { get; init; }
    public required string Message { get; init; }
    public required bool MutatesState { get; init; }
    public required bool AllowMutation { get; init; }
}
