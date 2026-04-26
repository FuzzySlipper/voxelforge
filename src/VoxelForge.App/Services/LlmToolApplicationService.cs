using System.Text;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.Core.Services;

namespace VoxelForge.App.Services;

public readonly record struct ApplyLlmMutationIntentsRequest(IReadOnlyList<VoxelMutationIntent> MutationIntents);

/// <summary>
/// Applies typed LLM tool results through the same editor mutation services used by other adapters.
/// </summary>
public sealed class LlmToolApplicationService
{
    private readonly VoxelEditingService _voxelEditingService;

    public LlmToolApplicationService(VoxelEditingService voxelEditingService)
    {
        _voxelEditingService = voxelEditingService;
    }

    public ApplicationServiceResult ApplyMutationIntents(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        ApplyLlmMutationIntentsRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.MutationIntents);

        if (request.MutationIntents.Count == 0)
        {
            return new ApplicationServiceResult
            {
                Success = true,
                Message = "No LLM mutations to apply.",
            };
        }

        var messages = new StringBuilder();
        var allEvents = new List<IApplicationEvent>();
        for (int i = 0; i < request.MutationIntents.Count; i++)
        {
            var result = _voxelEditingService.ApplyMutationIntent(
                document,
                undoStack,
                events,
                new ApplyVoxelMutationIntentRequest(request.MutationIntents[i]));
            if (!result.Success)
                return result;

            if (messages.Length > 0)
                messages.AppendLine();
            messages.Append(result.Message);
            allEvents.AddRange(result.Events);
        }

        return new ApplicationServiceResult
        {
            Success = true,
            Message = messages.ToString(),
            Events = allEvents,
        };
    }
}
