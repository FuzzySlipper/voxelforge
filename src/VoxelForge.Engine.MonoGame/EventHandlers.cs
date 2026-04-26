using VoxelForge.App.Events;
using VoxelForge.Engine.MonoGame.Rendering;
using VoxelForge.Engine.MonoGame.UI.Panels;

namespace VoxelForge.Engine.MonoGame;

internal sealed class VoxelRendererDirtyEventHandler :
    IEventHandler<VoxelModelChangedEvent>,
    IEventHandler<PaletteChangedEvent>,
    IEventHandler<ProjectLoadedEvent>,
    IEventHandler<UndoHistoryChangedEvent>
{
    private readonly VoxelRenderer _renderer;

    public VoxelRendererDirtyEventHandler(VoxelRenderer renderer)
    {
        _renderer = renderer;
    }

    public void Handle(VoxelModelChangedEvent applicationEvent) => _renderer.MarkDirty();

    public void Handle(PaletteChangedEvent applicationEvent) => _renderer.MarkDirty();

    public void Handle(ProjectLoadedEvent applicationEvent) => _renderer.MarkDirty();

    public void Handle(UndoHistoryChangedEvent applicationEvent) => _renderer.MarkDirty();
}

internal sealed class ReferenceModelRendererDirtyEventHandler : IEventHandler<ReferenceModelChangedEvent>
{
    private readonly ReferenceModelRenderer _renderer;

    public ReferenceModelRendererDirtyEventHandler(ReferenceModelRenderer renderer)
    {
        _renderer = renderer;
    }

    public void Handle(ReferenceModelChangedEvent applicationEvent) => _renderer.MarkDirty();
}

internal sealed class ReferenceModelPanelRefreshEventHandler : IEventHandler<ReferenceModelChangedEvent>
{
    private readonly ReferenceModelPanel _panel;

    public ReferenceModelPanelRefreshEventHandler(ReferenceModelPanel panel)
    {
        _panel = panel;
    }

    public void Handle(ReferenceModelChangedEvent applicationEvent) => _panel.Refresh();
}

internal sealed class ReferenceImagePanelRefreshEventHandler : IEventHandler<ReferenceImageChangedEvent>
{
    private readonly ReferenceImagePanel _panel;

    public ReferenceImagePanelRefreshEventHandler(ReferenceImagePanel panel)
    {
        _panel = panel;
    }

    public void Handle(ReferenceImageChangedEvent applicationEvent) => _panel.Rebuild();
}
