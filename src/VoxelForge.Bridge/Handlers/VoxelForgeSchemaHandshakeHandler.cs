using Den.Bridge.Abstractions;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles VoxelForge schema-level handshake (<c>voxelforge.handshake</c>) bridge commands.
/// Declares the sidecar's supported VoxelForge-specific capabilities
/// and schema version after den-bridge transport-level handshake.
/// </summary>
public sealed class VoxelForgeSchemaHandshakeHandler : IBridgeCommandHandler<VoxelForgeHandshakeRequest, VoxelForgeHandshakeResponse>
{
    private const string SupportedSchemaVersion = "voxelforge@1";
    private const string SchemaBundleId = "voxelforge-schema-2026-05-05";
    private static readonly string[] SupportedCapabilities = ["mesh_json", "incremental_mesh", "state_snapshot", "commands"];

    public ValueTask<VoxelForgeHandshakeResponse?> HandleAsync(
        VoxelForgeHandshakeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        // In this vertical slice, we accept "voxelforge@1" schema version.
        bool compatible = string.Equals(
            request.ClientSchemaVersion,
            SupportedSchemaVersion,
            StringComparison.OrdinalIgnoreCase);

        var response = new VoxelForgeHandshakeResponse
        {
            SidecarSchemaVersion = SupportedSchemaVersion,
            SupportedCapabilities = SupportedCapabilities,
            Compatible = compatible,
            SchemaBundleId = SchemaBundleId,
        };

        return ValueTask.FromResult<VoxelForgeHandshakeResponse?>(response);
    }
}