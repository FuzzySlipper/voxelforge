using Microsoft.Extensions.Logging;
using VoxelForge.App;
using VoxelForge.App.Console;
using VoxelForge.App.Console.Commands;
using VoxelForge.App.Services;
using VoxelForge.Core.Services;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Holds the explicit command catalog and a CommandRouter for bridge-accessible console commands.
/// Unknown/denied commands are rejected before reaching the router.
/// </summary>
public sealed class ConsoleCommandBridgeService
{
    private readonly CommandRouter _router;
    private readonly IReadOnlyDictionary<string, ConsoleCommandBridgeEntry> _entriesByName;

    public IReadOnlyDictionary<string, ConsoleCommandBridgeEntry> EntriesByName => _entriesByName;

    public ConsoleCommandBridgeService(
        VoxelForgeMcpSession session,
        ILoggerFactory loggerFactory,
        VoxelEditingService editingService,
        VoxelQueryService queryService,
        EditorConfigState config,
        EditorConfigService configService)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        // All bridge-accessible commands must be registered here explicitly.
        // Unknown console commands — or commands deemed dangerous in headless — are denied.
        var bridgeCommands = new List<IConsoleCommand>
        {
            // Read-only query commands
            new DescribeCommand(queryService),
            new GetVoxelCommand(queryService),
            new GetCubeCommand(queryService),
            new GetSphereCommand(queryService),
            new CountCommand(queryService),
            new ListFilesCommand(),

            // Mutating editing commands (require allow_mutation: true)
            new SetVoxelConsoleCommand(editingService),
            new RemoveVoxelConsoleCommand(editingService),
            new FillCommand(editingService),
            new ClearCommand(editingService),
            new GridCommand(editingService),
            new UndoCommand(),
            new RedoCommand(),
        };

        var logger = loggerFactory.CreateLogger<CommandRouter>();
        _router = new CommandRouter(bridgeCommands, logger);

        // Build metadata catalog
        var catalog = new List<ConsoleCommandBridgeEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cmd in bridgeCommands.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(cmd.Name))
                continue;

            var mutates = IsMutatingCommand(cmd.Name);
            catalog.Add(new ConsoleCommandBridgeEntry
            {
                Name = cmd.Name,
                Aliases = cmd.Aliases,
                HelpText = cmd.HelpText,
                MutatesState = mutates,
                BridgeNotes = mutates
                    ? "Mutates model state — requires allow_mutation: true."
                    : null,
            });
        }

        // Build lookup by primary name AND aliases, so "rm" maps to the "remove" entry.
        var dict = new Dictionary<string, ConsoleCommandBridgeEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            dict[entry.Name] = entry;
            foreach (var alias in entry.Aliases)
            {
                // Aliases only — skip if already taken by a primary name.
                dict.TryAdd(alias, entry);
            }
        }
        _entriesByName = dict;
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
    /// Conservative classification: commands that write to the model or document.
    /// </summary>
    private static bool IsMutatingCommand(string name) => name.ToLowerInvariant() switch
    {
        "set" or "s" or "place" => true,
        "remove" or "rm" or "delete" => true,
        "fill" or "f" => true,
        "clear" or "cls" => true,
        "grid" => true,
        "undo" => true,
        "redo" => true,
        // Read-only commands are everything else in the bridge catalog.
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
