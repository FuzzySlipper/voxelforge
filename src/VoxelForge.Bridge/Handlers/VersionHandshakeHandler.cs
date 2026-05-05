using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles version handshake requests from the Electron process.
/// </summary>
public sealed class VersionHandshakeHandler : IBridgeCommandHandler<VersionHandshakeRequest, VersionHandshakeResponse>
{
    private readonly string _appId;
    private readonly string _appVersion;

    public VersionHandshakeHandler(string appId, string appVersion)
    {
        _appId = appId;
        _appVersion = appVersion;
    }

    public ValueTask<VersionHandshakeResponse?> HandleAsync(
        VersionHandshakeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var compatible = string.Equals(
            request.ClientProtocolVersion,
            BridgeProtocol.ProtocolVersion,
            StringComparison.Ordinal);

        var response = new VersionHandshakeResponse
        {
            SidecarProtocolVersion = BridgeProtocol.ProtocolVersion,
            AppId = _appId,
            AppVersion = _appVersion,
            Compatible = compatible,
        };

        return ValueTask.FromResult<VersionHandshakeResponse?>(response);
    }
}
