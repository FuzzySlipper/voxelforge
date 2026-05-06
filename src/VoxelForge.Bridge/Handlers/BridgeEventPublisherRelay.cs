using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// DI-friendly relay for bridge event publishing. The WebSocket server is created
/// after the service provider, so bridge handlers depend on this relay and the
/// composition root attaches the actual server publisher once it starts.
/// </summary>
public sealed class BridgeEventPublisherRelay : IBridgeEventPublisher
{
    private IBridgeEventPublisher? _inner;

    public void Attach(IBridgeEventPublisher inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public ValueTask PublishAsync(BridgeEventFrame frame, CancellationToken cancellationToken = default)
    {
        var inner = _inner;
        if (inner is null)
            return ValueTask.CompletedTask;

        return inner.PublishAsync(frame, cancellationToken);
    }
}
