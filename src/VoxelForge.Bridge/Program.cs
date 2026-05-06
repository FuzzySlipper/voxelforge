using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Hosting;
using Den.Bridge.Protocol;
using Den.Bridge.Transport.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;
using VoxelForge.Core;
using VoxelForge.Core.Meshing;
using VoxelForge.App.Events;

namespace VoxelForge.Bridge;

public sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var appId = "voxelforge-bridge";
        var appVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var authToken = GenerateAuthToken();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "[HH:mm:ss] ";
        }));

        // Core/App services
        services.AddSingleton<ApplicationEventDispatcher>();
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<ApplicationEventDispatcher>());
        services.AddSingleton<IEventDispatcher>(provider => provider.GetRequiredService<ApplicationEventDispatcher>());
        services.AddSingleton<VoxelModelHolder>();
        services.AddSingleton<ProjectLifecycleService>();
        services.AddSingleton<VoxelEditingService>();
        services.AddSingleton<BridgeEventPublisherRelay>();
        services.AddSingleton<IBridgeEventPublisher>(provider => provider.GetRequiredService<BridgeEventPublisherRelay>());

        // Mesh and palette snapshot services
        services.AddSingleton<IVoxelMesher, GreedyMesher>();
        services.AddSingleton<MeshSnapshotService>();
        services.AddSingleton<PaletteSnapshotService>();
        services.AddSingleton<EditorSnapshotService>();

        // Mesh region and subscription services
        services.AddSingleton<MeshRegionService>();
        services.AddSingleton<MeshSubscriptionManager>();
        services.AddSingleton<MeshChangePushService>();
        services.AddSingleton<EditorUiStateBridgeService>();

        // Bridge handlers
        services.AddSingleton(new VersionHandshakeHandler(appId, appVersion));
        services.AddSingleton<VoxelForgeSchemaHandshakeHandler>();
        services.AddSingleton<MeshSnapshotHandler>();
        services.AddSingleton<MeshSubscribeHandler>();
        services.AddSingleton<MeshUnsubscribeHandler>();
        services.AddSingleton<PaletteGetHandler>();
        services.AddSingleton<EditorStateSubscribeHandler>();
        services.AddSingleton<EditorStateRequestFullHandler>();
        services.AddSingleton<CommandExecuteHandler>();
        services.AddSingleton<HistoryUndoHandler>();
        services.AddSingleton<HistoryRedoHandler>();
        services.AddSingleton<ProjectSaveHandler>();
        services.AddSingleton<ProjectLoadHandler>();

        // Register bridge host before building the provider so all services
        // (including model data) are in a single container.
        services.AddBridgeHost(registry =>
        {
            registry.RegisterCommand<PingRequest, PingResponse, PingHandler>("ping");
            registry.RegisterCommand<VersionHandshakeRequest, VersionHandshakeResponse, VersionHandshakeHandler>("version.handshake");
            registry.RegisterCommand<VoxelForgeHandshakeRequest, VoxelForgeHandshakeResponse, VoxelForgeSchemaHandshakeHandler>("voxelforge.handshake");
            registry.RegisterCommand<MeshSnapshotRequest, MeshSnapshotResponse, MeshSnapshotHandler>("voxelforge.mesh.request_snapshot");
            registry.RegisterCommand<MeshSubscribeRequest, MeshSubscribeResponse, MeshSubscribeHandler>("voxelforge.mesh.subscribe");
            registry.RegisterCommand<MeshUnsubscribeRequest, MeshUnsubscribeResponse, MeshUnsubscribeHandler>("voxelforge.mesh.unsubscribe");
            registry.RegisterCommand<PaletteGetRequest, PaletteGetResponse, PaletteGetHandler>("voxelforge.palette.get");
            registry.RegisterCommand<EditorStateSubscribeRequest, EditorStateSubscribeResponse, EditorStateSubscribeHandler>("voxelforge.state.subscribe");
            registry.RegisterCommand<EditorStateRequestFullRequest, EditorStateRequestFullResponse, EditorStateRequestFullHandler>("voxelforge.state.request_full");
            registry.RegisterCommand<CommandExecuteRequest, CommandExecuteResponse, CommandExecuteHandler>("voxelforge.command.execute");
            registry.RegisterCommand<HistoryUndoRequest, HistoryCommandResponse, HistoryUndoHandler>("voxelforge.history.undo");
            registry.RegisterCommand<HistoryRedoRequest, HistoryCommandResponse, HistoryRedoHandler>("voxelforge.history.redo");
            registry.RegisterCommand<ProjectSaveRequest, ProjectCommandResponse, ProjectSaveHandler>("voxelforge.project.save");
            registry.RegisterCommand<ProjectLoadRequest, ProjectCommandResponse, ProjectLoadHandler>("voxelforge.project.load");
            // Register event types
            registry.RegisterEvent<MeshUpdateEventPayload>("voxelforge.mesh.update");
            registry.RegisterEvent<PaletteUpdateEventPayload>("voxelforge.palette.update");
            registry.RegisterEvent<EditorStateDeltaEventPayload>("voxelforge.state.delta");
            registry.RegisterEvent<EditingLatencyEventPayload>("voxelforge.diagnostics.editing_latency");
        }, host =>
        {
            host.AppId = appId;
            host.AppVersion = appVersion;
            host.SupportedTransports = ["websocket"];
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Program>>();

        // Load model — either from --model argument or default test cube
        var modelHolder = provider.GetRequiredService<VoxelModelHolder>();
        var modelPath = args.FirstOrDefault(a => a.StartsWith("--model=", StringComparison.Ordinal));
        if (modelPath is not null)
        {
            var path = modelPath["--model=".Length..];
            modelHolder.LoadFromPath(path);
        }
        else
        {
            // Try to find house-with-windows.vforge in content directory
            var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
            var defaultModel = repoRoot is not null
                ? Path.Combine(repoRoot, "content", "house-with-windows.vforge")
                : null;

            if (defaultModel is not null && File.Exists(defaultModel))
            {
                modelHolder.LoadFromPath(defaultModel);
            }
            else
            {
                logger.LogWarning("No model file found; loading default test cube");
                modelHolder.LoadDefaultCube();
            }
        }

        var router = provider.GetRequiredService<IBridgeCommandRouter>();

        var serverOptions = new WebSocketBridgeServerOptions
        {
            Port = 0,
            AuthToken = authToken,
            Path = "/bridge",
        };

        await using var server = new WebSocketBridgeServer(serverOptions, router, provider.GetRequiredService<ILogger<WebSocketBridgeServer>>());
        await server.StartAsync();
        provider.GetRequiredService<BridgeEventPublisherRelay>().Attach(server);

        var endpoint = server.Endpoint;
        if (endpoint is null)
        {
            logger.LogError("Bridge WebSocket server failed to start; endpoint is null.");
            return 1;
        }

        logger.LogInformation("VoxelForge Bridge sidecar started.");
        logger.LogInformation("Endpoint: {Endpoint}", endpoint);
        logger.LogInformation("AuthToken: {AuthToken}", authToken);

        // Emit a machine-readable handshake line so the parent process (Electron) can discover
        // the endpoint and token without parsing log lines.
        var handshakeJson = JsonSerializer.Serialize(new
        {
            sidecar_ready = true,
            endpoint = endpoint.ToString(),
            auth_token = authToken,
            app_id = appId,
            app_version = appVersion,
        });
        Console.WriteLine($"[BRIDGE_HANDSHAKE]{handshakeJson}");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("VoxelForge Bridge sidecar shutting down.");
        return 0;
    }

    private static string GenerateAuthToken()
    {
        // 32 bytes of randomness, hex-encoded = 64 characters.
        var bytes = new byte[32];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? FindRepoRoot(string startPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(startPath));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "voxelforge.slnx")))
                return dir;
            dir = dir.Length > 3 ? Path.GetDirectoryName(dir) : null;
        }
        return null;
    }
}