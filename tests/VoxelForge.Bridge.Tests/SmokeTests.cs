using System.Text.Json;
using Den.Bridge.Abstractions;
using Den.Bridge.Hosting;
using Den.Bridge.Protocol;
using Den.Bridge.Transport.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Bridge.Handlers;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Tests;

public sealed class SmokeTests : IAsyncLifetime
{
    private WebSocketBridgeServer? _server;
    private WebSocketBridgeClient? _client;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBridgeCommandRouter>(new DelegatingRouter((request, _) =>
        {
            return ValueTask.FromResult(BridgeResponseFrame.Success(request.RequestId));
        }));

        var provider = services.BuildServiceProvider();
        var router = provider.GetRequiredService<IBridgeCommandRouter>();

        _server = new WebSocketBridgeServer(
            new WebSocketBridgeServerOptions { Port = 0, AuthToken = "test-token", Path = "/bridge" },
            router,
            NullLogger<WebSocketBridgeServer>.Instance);

        await _server.StartAsync();

        _client = await WebSocketBridgeClient.ConnectAsync(new WebSocketBridgeClientOptions
        {
            Endpoint = _server.Endpoint!,
            AuthToken = "test-token",
        });
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task PingHandler_EchoesMessageAndTimestamp()
    {
        var handler = new PingHandler();
        var context = new BridgeRequestContext("req-1", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new PingRequest { Message = "hello-smoke" },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("hello-smoke", response.Echo);
        Assert.True(response.Timestamp > 0);
    }

    [Fact]
    public async Task VersionHandshakeHandler_ReportsCompatibleWhenVersionsMatch()
    {
        var handler = new VersionHandshakeHandler("voxelforge-test", "0.1.0");
        var context = new BridgeRequestContext("req-2", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VersionHandshakeRequest { ClientProtocolVersion = BridgeProtocol.ProtocolVersion },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(BridgeProtocol.ProtocolVersion, response.SidecarProtocolVersion);
        Assert.Equal("voxelforge-test", response.AppId);
        Assert.Equal("0.1.0", response.AppVersion);
        Assert.True(response.Compatible);
    }

    [Fact]
    public async Task VersionHandshakeHandler_ReportsIncompatibleWhenVersionsDiffer()
    {
        var handler = new VersionHandshakeHandler("voxelforge-test", "0.1.0");
        var context = new BridgeRequestContext("req-3", BridgeCorrelation.Empty, (_, __) => ValueTask.CompletedTask);

        var response = await handler.HandleAsync(
            new VersionHandshakeRequest { ClientProtocolVersion = "99.99" },
            context,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.False(response.Compatible);
    }

    [Fact]
    public async Task ClientAndServer_RoutePingRequestAndResponse()
    {
        Assert.NotNull(_client);
        Assert.NotNull(_server);

        // We need a real server with real handlers for this test.
        await _server.DisposeAsync();
        await _client.DisposeAsync();

        var handlerServices = new ServiceCollection();
        handlerServices.AddSingleton(new VersionHandshakeHandler("voxelforge-smoke", "0.0.0"));
        handlerServices.AddBridgeHost(registry =>
        {
            registry.RegisterCommand<PingRequest, PingResponse, PingHandler>("ping");
            registry.RegisterCommand<VersionHandshakeRequest, VersionHandshakeResponse, VersionHandshakeHandler>("version.handshake");
        });

        var handlerProvider = handlerServices.BuildServiceProvider();
        var realRouter = handlerProvider.GetRequiredService<IBridgeCommandRouter>();

        _server = new WebSocketBridgeServer(
            new WebSocketBridgeServerOptions { Port = 0, AuthToken = "test-token", Path = "/bridge" },
            realRouter,
            NullLogger<WebSocketBridgeServer>.Instance);
        await _server.StartAsync();

        _client = await WebSocketBridgeClient.ConnectAsync(new WebSocketBridgeClientOptions
        {
            Endpoint = _server.Endpoint!,
            AuthToken = "test-token",
        });

        var request = new BridgeRequestFrame
        {
            RequestId = "req-ping",
            Command = "ping",
            Payload = BridgeJson.ToElement(new PingRequest { Message = "smoke-ping" }),
        };

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        Assert.Equal("smoke-ping", response.Result.Value.GetProperty("echo").GetString());
        Assert.True(response.Result.Value.GetProperty("timestamp").GetInt64() > 0);
    }

    [Fact]
    public async Task ClientAndServer_RouteVersionHandshakeRequestAndResponse()
    {
        Assert.NotNull(_client);
        Assert.NotNull(_server);

        // Rebuild with real handlers if the previous test didn't already.
        if (_server is { Port: 0 })
        {
            // Already rebuilt in previous test, but safe to rebuild again.
        }

        await _server.DisposeAsync();
        await _client.DisposeAsync();

        var handlerServices = new ServiceCollection();
        handlerServices.AddSingleton(new VersionHandshakeHandler("voxelforge-smoke", "0.0.0"));
        handlerServices.AddBridgeHost(registry =>
        {
            registry.RegisterCommand<PingRequest, PingResponse, PingHandler>("ping");
            registry.RegisterCommand<VersionHandshakeRequest, VersionHandshakeResponse, VersionHandshakeHandler>("version.handshake");
        });

        var handlerProvider = handlerServices.BuildServiceProvider();
        var realRouter = handlerProvider.GetRequiredService<IBridgeCommandRouter>();

        _server = new WebSocketBridgeServer(
            new WebSocketBridgeServerOptions { Port = 0, AuthToken = "test-token", Path = "/bridge" },
            realRouter,
            NullLogger<WebSocketBridgeServer>.Instance);
        await _server.StartAsync();

        _client = await WebSocketBridgeClient.ConnectAsync(new WebSocketBridgeClientOptions
        {
            Endpoint = _server.Endpoint!,
            AuthToken = "test-token",
        });

        var request = new BridgeRequestFrame
        {
            RequestId = "req-version",
            Command = "version.handshake",
            Payload = BridgeJson.ToElement(new VersionHandshakeRequest { ClientProtocolVersion = BridgeProtocol.ProtocolVersion }),
        };

        var response = await _client.SendAsync(request, CancellationToken.None);

        Assert.Null(response.Error);
        Assert.NotNull(response.Result);
        Assert.Equal(BridgeProtocol.ProtocolVersion, response.Result.Value.GetProperty("sidecar_protocol_version").GetString());
        Assert.True(response.Result.Value.GetProperty("compatible").GetBoolean());
    }

    private sealed class DelegatingRouter : IBridgeCommandRouter
    {
        private readonly Func<BridgeRequestFrame, CancellationToken, ValueTask<BridgeResponseFrame>> _dispatchAsync;

        public DelegatingRouter(Func<BridgeRequestFrame, CancellationToken, ValueTask<BridgeResponseFrame>> dispatchAsync)
        {
            _dispatchAsync = dispatchAsync;
        }

        public ValueTask<BridgeResponseFrame> DispatchAsync(
            BridgeRequestFrame request,
            CancellationToken cancellationToken = default)
        {
            return _dispatchAsync(request, cancellationToken);
        }
    }
}
