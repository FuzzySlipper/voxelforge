using System.Text.Json.Serialization;

namespace VoxelForge.Mcp.Tools;

/// <summary>
/// Metadata for a single console command exposed via the guarded manual console bridge.
/// </summary>
public sealed record ConsoleCommandBridgeEntry
{
    /// <summary>The primary command name that maps to IConsoleCommand.Name.</summary>
    public required string Name { get; init; }

    /// <summary>Alternate command names (IConsoleCommand.Aliases).</summary>
    public string[] Aliases { get; init; } = [];

    /// <summary>Human-readable help/usage text from the underlying command.</summary>
    public required string HelpText { get; init; }

    /// <summary>
    /// True when this command mutates model/document state.
    /// Such commands require <c>allow_mutation: true</c> in the tool call.
    /// </summary>
    public bool MutatesState { get; init; }

    /// <summary>
    /// True when the bridge requires explicit opt-in (<c>allow_mutation: true</c>).
    /// Always true when <see cref="MutatesState"/> is true.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresAllowMutation => MutatesState;

    /// <summary>
    /// Optional note about bridge support status or denial reason.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BridgeNotes { get; init; }
}
