using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App.Services;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles <c>voxelforge.mesh.unsubscribe</c> bridge commands.
/// Cancels a prior mesh update subscription.
/// </summary>
public sealed class MeshUnsubscribeHandler : IBridgeCommandHandler<MeshUnsubscribeRequest, MeshUnsubscribeResponse>
{
    private readonly MeshSubscriptionManager _subscriptionManager;

    public MeshUnsubscribeHandler(MeshSubscriptionManager subscriptionManager)
    {
        _subscriptionManager = subscriptionManager;
    }

    public ValueTask<MeshUnsubscribeResponse?> HandleAsync(
        MeshUnsubscribeRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        _subscriptionManager.Unsubscribe(request.SubscriptionId);

        return ValueTask.FromResult<MeshUnsubscribeResponse?>(new MeshUnsubscribeResponse
        {
            SubscriptionId = request.SubscriptionId,
        });
    }
}