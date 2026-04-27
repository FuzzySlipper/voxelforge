using VoxelForge.App.Services;
using VoxelForge.Core.LLM.Handlers;

namespace VoxelForge.Mcp.Tools;

public sealed class GetModelInfoMcpTool : LlmToolMcpTool
{
    public GetModelInfoMcpTool(
        GetModelInfoHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: true)
    {
    }
}

public sealed class GetModelInfoServerTool : VoxelForgeMcpServerTool
{
    public GetModelInfoServerTool(GetModelInfoMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class SetVoxelsMcpTool : LlmToolMcpTool
{
    public SetVoxelsMcpTool(
        SetVoxelsHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: false)
    {
    }
}

public sealed class SetVoxelsServerTool : VoxelForgeMcpServerTool
{
    public SetVoxelsServerTool(SetVoxelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class RemoveVoxelsMcpTool : LlmToolMcpTool
{
    public RemoveVoxelsMcpTool(
        RemoveVoxelsHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: false)
    {
    }
}

public sealed class RemoveVoxelsServerTool : VoxelForgeMcpServerTool
{
    public RemoveVoxelsServerTool(RemoveVoxelsMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class GetVoxelsInAreaMcpTool : LlmToolMcpTool
{
    public GetVoxelsInAreaMcpTool(
        GetVoxelsInAreaHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: true)
    {
    }
}

public sealed class GetVoxelsInAreaServerTool : VoxelForgeMcpServerTool
{
    public GetVoxelsInAreaServerTool(GetVoxelsInAreaMcpTool tool)
        : base(tool)
    {
    }
}

public sealed class ApplyVoxelPrimitivesMcpTool : LlmToolMcpTool
{
    public ApplyVoxelPrimitivesMcpTool(
        ApplyVoxelPrimitivesHandler handler,
        VoxelForgeMcpSession session,
        LlmToolApplicationService applicationService)
        : base(handler, session, applicationService, isReadOnly: false)
    {
    }
}

public sealed class ApplyVoxelPrimitivesServerTool : VoxelForgeMcpServerTool
{
    public ApplyVoxelPrimitivesServerTool(ApplyVoxelPrimitivesMcpTool tool)
        : base(tool)
    {
    }
}
