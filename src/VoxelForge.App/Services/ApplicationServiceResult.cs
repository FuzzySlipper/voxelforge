using VoxelForge.App.Events;

namespace VoxelForge.App.Services;

public class ApplicationServiceResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<IApplicationEvent> Events { get; init; } = [];
}

public sealed class ApplicationServiceResult<TData> : ApplicationServiceResult
{
    public TData? Data { get; init; }
}
