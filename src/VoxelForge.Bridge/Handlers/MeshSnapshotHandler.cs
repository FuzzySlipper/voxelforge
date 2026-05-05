using System.Diagnostics;
using Den.Bridge.Abstractions;
using Den.Bridge.Protocol;
using VoxelForge.App.Services;
using VoxelForge.App.Snapshots;
using VoxelForge.Bridge.Protocol;

namespace VoxelForge.Bridge.Handlers;

/// <summary>
/// Handles <c>voxelforge.mesh.request_snapshot</c> bridge commands.
/// Produces a renderer-neutral mesh snapshot from the sidecar's current model
/// using <see cref="MeshSnapshotService"/>.
/// <para>
/// This is a C#-owned, read-only command per the bridge protocol.
/// TS requests mesh data; C# responds with authoritative geometry.
/// </para>
/// </summary>
public sealed class MeshSnapshotHandler : IBridgeCommandHandler<MeshSnapshotRequest, MeshSnapshotResponse>
{
    private readonly VoxelModelHolder _modelHolder;
    private readonly MeshSnapshotService _meshService;
    private readonly PaletteSnapshotService _paletteService;

    public MeshSnapshotHandler(
        VoxelModelHolder modelHolder,
        MeshSnapshotService meshService,
        PaletteSnapshotService paletteService)
    {
        _modelHolder = modelHolder;
        _meshService = meshService;
        _paletteService = paletteService;
    }

    public async ValueTask<MeshSnapshotResponse?> HandleAsync(
        MeshSnapshotRequest request,
        BridgeRequestContext context,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();

        if (!_modelHolder.IsLoaded)
        {
            throw new BridgeHandlerException(
                "voxelforge.mesh.not_loaded",
                "No model is currently loaded. Load a model before requesting a mesh snapshot.",
                BridgeErrorCategories.NotFound,
                retryable: true);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var model = _modelHolder.Model;
        var meshStopwatch = Stopwatch.StartNew();
        var mesh = _meshService.BuildSnapshot(model);
        meshStopwatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var serializeStopwatch = Stopwatch.StartNew();
        var response = MapToResponse(mesh, model, request, _modelHolder.ModelId);
        serializeStopwatch.Stop();

        totalStopwatch.Stop();

        response.Metrics = new MeshSnapshotMetrics
        {
            MeshGenerationMs = meshStopwatch.ElapsedMilliseconds,
            SerializationMs = serializeStopwatch.ElapsedMilliseconds,
            TotalMs = totalStopwatch.ElapsedMilliseconds,
        };

        return await ValueTask.FromResult(response);
    }

    private static MeshSnapshotResponse MapToResponse(
        MeshSnapshot mesh,
        Core.VoxelModel model,
        MeshSnapshotRequest request,
        string modelId)
    {
        var bounds = mesh.Bounds is not null
            ? new BoundsDto
            {
                MinX = mesh.Bounds.MinX,
                MinY = mesh.Bounds.MinY,
                MinZ = mesh.Bounds.MinZ,
                MaxX = mesh.Bounds.MaxX,
                MaxY = mesh.Bounds.MaxY,
                MaxZ = mesh.Bounds.MaxZ,
            }
            : null;

        Dictionary<string, PaletteEntryDto>? paletteMapping = null;
        if (request.IncludePaletteMapping && mesh.PaletteIndices is not null)
        {
            paletteMapping = [];
            foreach (var (idx, mat) in model.Palette.Entries)
            {
                paletteMapping[idx.ToString()] = new PaletteEntryDto
                {
                    Name = mat.Name,
                    Color = $"#{mat.Color.R:X2}{mat.Color.G:X2}{mat.Color.B:X2}",
                    A = mat.Color.A,
                    Visible = true,
                };
            }
        }

        return new MeshSnapshotResponse
        {
            ModelId = string.IsNullOrEmpty(request.ModelId) ? modelId : request.ModelId,
            MeshId = $"mesh-{modelId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Format = request.PayloadFormat,
            VertexCount = mesh.VertexCount,
            IndexCount = mesh.Indices.Length,
            TriangleCount = mesh.TriangleCount,
            Positions = mesh.Positions,
            Normals = mesh.Normals,
            Colors = mesh.Colors,
            PaletteIndices = mesh.PaletteIndices,
            Indices = mesh.Indices,
            Bounds = bounds,
            PaletteMapping = paletteMapping,
        };
    }
}