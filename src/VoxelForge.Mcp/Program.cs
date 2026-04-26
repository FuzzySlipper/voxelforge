using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using VoxelForge.App;
using VoxelForge.Mcp;
using VoxelForge.Mcp.Tools;

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
builder.Services.AddVoxelForgeMcpTools();
builder.Services.AddMcpServer()
    .WithHttpTransport();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "VoxelForge.Mcp",
    mcp_endpoint = "/mcp",
}));

app.MapGet("/health", (VoxelForgeMcpOptions currentOptions, VoxelForgeMcpSession session) => Results.Ok(new
{
    status = "healthy",
    project_directory = currentOptions.GetResolvedProjectDirectory(),
    voxel_count = session.Document.Model.GetVoxelCount(),
    region_count = session.Document.Labels.Regions.Count,
    tool_endpoint = "/mcp",
}));

app.MapMcp("/mcp");

app.Run();

public partial class Program;
