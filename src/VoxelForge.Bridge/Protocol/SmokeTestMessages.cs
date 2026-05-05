namespace VoxelForge.Bridge.Protocol;

/// <summary>
/// Ping request sent by the Electron renderer/main process to verify connectivity.
/// </summary>
public sealed class PingRequest
{
    public string Message { get; set; } = "ping";
}

/// <summary>
/// Ping response returned by the C# sidecar.
/// </summary>
public sealed class PingResponse
{
    public required string Echo { get; set; }

    public required long Timestamp { get; set; }
}

/// <summary>
/// Version handshake request sent by the Electron process.
/// </summary>
public sealed class VersionHandshakeRequest
{
    public string ClientProtocolVersion { get; set; } = "1.0";
}

/// <summary>
/// Version handshake response returned by the C# sidecar.
/// </summary>
public sealed class VersionHandshakeResponse
{
    public required string SidecarProtocolVersion { get; set; }

    public required string AppId { get; set; }

    public required string AppVersion { get; set; }

    public required bool Compatible { get; set; }
}
