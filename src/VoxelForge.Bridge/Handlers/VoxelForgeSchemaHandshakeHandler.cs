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

    // Capability naming convention:
    //   mesh_* capabilities (mesh_json, mesh_binary) map to payload_format values.
    //   state_* capabilities (state_snapshot, state_delta) map to delivery_mode values.
    // The delivery_mode values drop the state_ prefix for brevity ("snapshot", "delta").
    // This is intentional and mirrors how mesh capabilities drop mesh_ for format values.
    private static readonly string[] SupportedCapabilities = ["mesh_json", "incremental_mesh", "state_snapshot", "state_delta", "commands", "history", "project_io"];

    public ValueTask<VoxelForgeHandshakeResponse?> HandleAsync(
        VoxelForgeHandshakeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        // Strict exact-match for v1. Per the bridge protocol doc, major schema
        // version mismatches are fatal: "voxelforge@1" must match exactly.
        // Minor version bumps ("voxelforge@1.0" → "voxelforge@1.1") add optional
        // fields and forward-compatible commands; TS clients must ignore unknown
        // fields. This strict v1 policy is intentional — the schema is still
        // experimental and should not silently accept unknown major versions.
        // Future versions may negotiate a set of supported versions.
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