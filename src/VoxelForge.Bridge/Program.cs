using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Hosting;
using Den.Bridge.Transport.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;

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

        services.AddSingleton(new VersionHandshakeHandler(appId, appVersion));
        services.AddBridgeHost(registry =>
        {
            registry.RegisterCommand<PingRequest, PingResponse, PingHandler>("ping");
            registry.RegisterCommand<VersionHandshakeRequest, VersionHandshakeResponse, VersionHandshakeHandler>("version.handshake");
        }, host =>
        {
            host.AppId = appId;
            host.AppVersion = appVersion;
            host.SupportedTransports = ["websocket"];
        });

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<Program>>();
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
}
