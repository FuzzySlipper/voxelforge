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
        services.AddSingleton<ILoggerFactory>(sp => sp.GetRequiredService<ILoggerFactory>());

        // VoxelModelHolder — holds the currently loaded model
        services.AddSingleton<VoxelModelHolder>();

        // Mesh and palette snapshot services
        services.AddSingleton<IVoxelMesher, GreedyMesher>();
        services.AddSingleton<MeshSnapshotService>();
        services.AddSingleton<PaletteSnapshotService>();
        services.AddSingleton<EditorSnapshotService>();

        // Bridge handlers
        services.AddSingleton(new VersionHandshakeHandler(appId, appVersion));
        services.AddSingleton<VoxelForgeSchemaHandshakeHandler>();
        services.AddSingleton<MeshSnapshotHandler>();
        services.AddSingleton<PaletteGetHandler>();

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

        services.AddBridgeHost(registry =>
        {
            registry.RegisterCommand<PingRequest, PingResponse, PingHandler>("ping");
            registry.RegisterCommand<VersionHandshakeRequest, VersionHandshakeResponse, VersionHandshakeHandler>("version.handshake");
            registry.RegisterCommand<VoxelForgeHandshakeRequest, VoxelForgeHandshakeResponse, VoxelForgeSchemaHandshakeHandler>("voxelforge.handshake");
            registry.RegisterCommand<MeshSnapshotRequest, MeshSnapshotResponse, MeshSnapshotHandler>("voxelforge.mesh.request_snapshot");
            registry.RegisterCommand<PaletteGetRequest, PaletteGetResponse, PaletteGetHandler>("voxelforge.palette.get");
        }, host =>
        {
            host.AppId = appId;
            host.AppVersion = appVersion;
            host.SupportedTransports = ["websocket"];
        });

        // Rebuild provider after AddBridgeHost
        provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IBridgeCommandRouter>();

        var serverOptions = new WebSocketBridgeServerOptions
        {
            Port = 0,
            AuthToken = authToken,
            Path = "/bridge",
        };

        await using var server = new WebSocketBridgeServer(serverOptions, router, provider.GetRequiredService<ILogger<WebSocketBridgeServer>>());
        await server.StartAsync();

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