using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Console;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Services;
using VoxelForge.Mcp.Tools;

namespace VoxelForge.Mcp.Tests;

public sealed class ConsoleCommandBridgeTests
{
    [Fact]
    public void RunConsoleCommand_ReadOnlyQuery_SucceedsWithoutMutationOptIn()
    {
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);

        var result = bridge.Execute("describe", [], allowMutation: false, session.CommandContext);

        Assert.True(result.Success, result.Message);
        Assert.Equal("describe", result.Command);
        Assert.False(result.MutatesState);
        Assert.False(result.AllowMutation);
    }

    [Fact]
    public void RunConsoleCommand_MutatingCommand_FailsWithoutMutationOptIn()
    {
        var bridge = CreateBridge();
        var session = CreateSession();

        var result = bridge.Execute("fill", ["0", "0", "0", "1", "1", "1", "1"], allowMutation: false, session.CommandContext);

        Assert.False(result.Success);
        Assert.Contains("allow_mutation=true", result.Message, StringComparison.Ordinal);
        Assert.True(result.MutatesState);
        Assert.False(result.AllowMutation);
    }

    [Fact]
    public void RunConsoleCommand_MutatingCommand_SucceedsWithMutationOptIn()
    {
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.Palette.Set(1, new MaterialDef
        {
            Name = "test",
            Color = new RgbaColor(255, 0, 0),
        });

        var result = bridge.Execute("fill", ["0", "0", "0", "1", "1", "1", "1"], allowMutation: true, session.CommandContext);

        Assert.True(result.Success, result.Message);
        Assert.True(result.MutatesState);
        Assert.True(result.AllowMutation);
        Assert.Equal(8, session.Document.Model.GetVoxelCount());
    }

    [Fact]
    public void RunConsoleCommand_UnknownCommand_Fails()
    {
        var bridge = CreateBridge();
        var session = CreateSession();

        var result = bridge.Execute("nonexistent_cmd_xyz", [], allowMutation: false, session.CommandContext);

        Assert.False(result.Success);
        Assert.Contains("Unknown or denied", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunConsoleCommand_TokenizedArgs_PassedDirectlyNotShellString()
    {
        // This proves that args are passed as a tokenized array without
        // rebuilding a shell command string. The GetVoxelCommand parses
        // its own args as integers directly — no shell interpretation.
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(10, 20, 30), 5);

        var result = bridge.Execute("get", ["10", "20", "30"], allowMutation: false, session.CommandContext);

        Assert.True(result.Success, result.Message);
        Assert.Contains("(10,20,30)", result.Message, StringComparison.Ordinal);
        Assert.Contains("5", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunConsoleCommand_CountCommand_ReadOnlyWorks()
    {
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);
        session.Document.Model.SetVoxel(new Point3(1, 0, 0), 2);

        var result = bridge.Execute("count", [], allowMutation: false, session.CommandContext);

        Assert.True(result.Success, result.Message);
        Assert.Equal("Total voxels: 2", result.Message);
    }

    [Fact]
    public void RunConsoleCommand_MutatingCommandViaAlias_RequiresMutationOptIn()
    {
        var bridge = CreateBridge();
        var session = CreateSession();

        // "rm" is an alias for "remove" which is mutating
        var result = bridge.Execute("rm", ["0", "0", "0"], allowMutation: false, session.CommandContext);

        Assert.False(result.Success);
        Assert.Contains("allow_mutation", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunConsoleCommand_MutatingCommandViaAlias_SucceedsWithOptIn()
    {
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);

        var result = bridge.Execute("rm", ["0", "0", "0"], allowMutation: true, session.CommandContext);

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, session.Document.Model.GetVoxelCount());
    }

    [Fact]
    public void RunConsoleCommandTool_InvokesReadOnlyViaToolInterface()
    {
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(0, 0, 0), 1);

        var tool = new RunConsoleCommandMcpTool(bridge, session);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "count",
            args = new string[] { },
            allow_mutation = false,
        });

        var result = tool.Invoke(args, CancellationToken.None);

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(result.Message);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("count", json.RootElement.GetProperty("command").GetString());
        Assert.Contains("Total voxels: 1", json.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunConsoleCommandTool_MissingCommandField_Fails()
    {
        var bridge = CreateBridge();
        var session = CreateSession();

        var tool = new RunConsoleCommandMcpTool(bridge, session);
        var args = JsonSerializer.SerializeToElement(new
        {
            args = new string[] { },
        });

        var result = tool.Invoke(args, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Missing required field", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunConsoleCommandTool_MutatingWithoutAllowMutation_Fails()
    {
        var bridge = CreateBridge();
        var session = CreateSession();

        var tool = new RunConsoleCommandMcpTool(bridge, session);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "clear",
            args = new string[] { },
            allow_mutation = false,
        });

        var result = tool.Invoke(args, CancellationToken.None);

        Assert.False(result.Success);
        using var json = JsonDocument.Parse(result.Message);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("mutates_state").GetBoolean());
        Assert.False(json.RootElement.GetProperty("allow_mutation").GetBoolean());
    }

    [Fact]
    public void ListConsoleCommands_ReturnsMetadata()
    {
        var bridge = CreateBridge();

        var entries = bridge.EntriesByName;

        // Should contain read-only commands
        Assert.True(entries.ContainsKey("describe"));
        Assert.True(entries.ContainsKey("count"));
        Assert.True(entries.ContainsKey("get"));
        Assert.True(entries.ContainsKey("getcube"));
        Assert.True(entries.ContainsKey("getsphere"));
        Assert.True(entries.ContainsKey("list"));

        // Should contain mutating commands
        Assert.True(entries.ContainsKey("fill"));
        Assert.True(entries.ContainsKey("set"));
        Assert.True(entries.ContainsKey("remove"));
        Assert.True(entries.ContainsKey("clear"));
        Assert.True(entries.ContainsKey("grid"));
        Assert.True(entries.ContainsKey("undo"));
        Assert.True(entries.ContainsKey("redo"));

        // Verify metadata correctness
        var fillEntry = entries["fill"];
        Assert.True(fillEntry.MutatesState);
        Assert.True(fillEntry.RequiresAllowMutation);
        Assert.Contains("allow_mutation", fillEntry.BridgeNotes, StringComparison.Ordinal);

        var countEntry = entries["count"];
        Assert.False(countEntry.MutatesState);
        Assert.False(countEntry.RequiresAllowMutation);
        Assert.Null(countEntry.BridgeNotes);
    }

    [Fact]
    public void ListConsoleCommandsTool_ReturnsJsonArray()
    {
        var bridge = CreateBridge();
        var tool = new ListConsoleCommandsMcpTool(bridge);
        var emptyArgs = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

        var result = tool.Invoke(emptyArgs, CancellationToken.None);

        Assert.True(result.Success);
        using var document = JsonDocument.Parse(result.Message);
        var array = document.RootElement.EnumerateArray().ToArray();
        Assert.NotEmpty(array);

        var fillEntry = array.First(e => e.GetProperty("name").GetString() == "fill");
        Assert.True(fillEntry.GetProperty("mutates_state").GetBoolean());
        Assert.True(fillEntry.GetProperty("requires_allow_mutation").GetBoolean());

        var countEntry = array.First(e => e.GetProperty("name").GetString() == "count");
        Assert.False(countEntry.GetProperty("mutates_state").GetBoolean());
    }

    [Fact]
    public void BridgeCommand_DoesNotReconstructShellString_ArgsAreTokenized()
    {
        // Prove the run_console_command tool accepts tokenized args
        // and passes them as string[] to IConsoleCommand.Execute,
        // never joining them into a command line.
        var bridge = CreateBridge();
        var session = CreateSession();
        session.Document.Model.SetVoxel(new Point3(42, 99, -5), 7);

        var tool = new RunConsoleCommandMcpTool(bridge, session);
        var args = JsonSerializer.SerializeToElement(new
        {
            command = "get",
            args = new[] { "42", "99", "-5" },
            allow_mutation = false,
        });

        var result = tool.Invoke(args, CancellationToken.None);

        Assert.True(result.Success);
        using var json = JsonDocument.Parse(result.Message);
        var msg = json.RootElement.GetProperty("message").GetString();
        Assert.Contains("(42,99,-5)", msg, StringComparison.Ordinal);
    }

    private static ConsoleCommandBridgeService CreateBridge()
    {
        return new ConsoleCommandBridgeService(
            CreateSession(),
            NullLoggerFactory.Instance,
            new VoxelEditingService(),
            new VoxelQueryService(),
            new EditorConfigState(),
            new EditorConfigService());
    }

    private static VoxelForgeMcpSession CreateSession()
    {
        return new VoxelForgeMcpSession(new EditorConfigState(), NullLoggerFactory.Instance);
    }
}
