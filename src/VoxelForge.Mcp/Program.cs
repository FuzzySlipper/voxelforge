using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using VoxelForge.App;
using VoxelForge.App.Services;
using VoxelForge.Core.Meshing;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Tools;
using VoxelForge.Mcp.Viewer;
using VoxelForge.Mcp.Services;

var builder = WebApplication.CreateBuilder(args);

var options = new VoxelForgeMcpOptions();
builder.Configuration.GetSection("VoxelForgeMcp").Bind(options);
if (builder.Configuration["port"] is { } port)
    options.ListenUrl = $"http://localhost:{port}";
if (builder.Configuration["listen-url"] is { } listenUrl)
    options.ListenUrl = listenUrl;
if (builder.Configuration["project-dir"] is { } projectDirectory)
    options.ProjectDirectory = projectDirectory;

Directory.CreateDirectory(options.GetResolvedProjectDirectory());

builder.WebHost.UseUrls(options.ListenUrl);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(EditorConfigState.Load());
builder.Services.AddSingleton<VoxelForgeMcpSession>();

// Register viewer services
builder.Services.AddSingleton<IVoxelMesher>(_ => new GreedyMesher());
builder.Services.AddSingleton<MeshSnapshotService>();
builder.Services.AddSingleton<PaletteSnapshotService>();

// Register viewer capture service (Chromium headless browser screenshot)
builder.Services.AddSingleton<IViewerCaptureService>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<ChromiumViewerCaptureService>>();
    var mcpOptions = sp.GetRequiredService<VoxelForgeMcpOptions>();
    var projectDir = mcpOptions.GetResolvedProjectDirectory();
    var capturesDir = Path.Combine(projectDir, "mcp", "captures");
    var baseUrl = mcpOptions.ListenUrl;
    // Use the listen URL as the viewer base URL for local Chromium capture
    return new ChromiumViewerCaptureService(baseUrl, capturesDir, logger);
});

builder.Services.AddVoxelForgeMcpTools();
builder.Services.AddMcpServer()
    .WithHttpTransport();

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () => Results.Ok(new
{
    name = "VoxelForge.Mcp",
    mcp_endpoint = "/mcp",
    viewer_endpoint = "/viewer",
}));

app.MapGet("/health", (VoxelForgeMcpOptions currentOptions, VoxelForgeMcpSession session) => Results.Ok(new
{
    status = "healthy",
    project_directory = currentOptions.GetResolvedProjectDirectory(),
    voxel_count = session.Document.Model.GetVoxelCount(),
    region_count = session.Document.Labels.Regions.Count,
    tool_endpoint = "/mcp",
    viewer_endpoint = "/viewer",
}));

app.MapMcp("/mcp");
app.MapViewerEndpoints();

app.Run();

public partial class Program;
