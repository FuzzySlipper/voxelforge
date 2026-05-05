using Den.Bridge.Abstractions;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles bridge ping requests from the Electron process.
/// </summary>
public sealed class PingHandler : IBridgeCommandHandler<PingRequest, PingResponse>
{
    public ValueTask<PingResponse?> HandleAsync(
        PingRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var response = new PingResponse
        {
            Echo = request.Message,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        return ValueTask.FromResult<PingResponse?>(response);
    }
}
