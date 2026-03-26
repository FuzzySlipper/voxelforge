using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.Core.Vox;

namespace VoxelForge.Core.Tests;

public sealed class VoxImportExportTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;
    private static VoxelModel CreateModel() => new(NullLogger<VoxelModel>.Instance);

    [Fact]
    public void ExportThenImport_RoundTrip_PreservesVoxels()
    {
        var model = CreateModel();
        model.Palette.Set(1, new MaterialDef { Name = "Red", Color = new RgbaColor(255, 0, 0) });
        model.Palette.Set(2, new MaterialDef { Name = "Blue", Color = new RgbaColor(0, 0, 255) });

        model.SetVoxel(new Point3(0, 0, 0), 1);
        model.SetVoxel(new Point3(1, 0, 0), 1);
        model.SetVoxel(new Point3(0, 1, 0), 2);
        model.SetVoxel(new Point3(5, 5, 5), 2);

        // Export
        var stream = new MemoryStream();
        var exporter = new VoxExporter(NullLogger<VoxExporter>.Instance);
        exporter.Export(model, stream);

        // Import
        stream.Position = 0;
        var importer = new VoxImporter(LogFactory);
        var imported = importer.Import(stream);

        Assert.Single(imported);
        var result = imported[0];
        Assert.Equal(4, result.GetVoxelCount());
    }

    [Fact]
    public void ExportThenImport_PreservesColors()
    {
        var model = CreateModel();
        model.Palette.Set(1, new MaterialDef { Name = "Green", Color = new RgbaColor(0, 255, 0) });
        model.SetVoxel(new Point3(0, 0, 0), 1);

        var stream = new MemoryStream();
        new VoxExporter(NullLogger<VoxExporter>.Instance).Export(model, stream);

        stream.Position = 0;
        var result = new VoxImporter(LogFactory).Import(stream)[0];

        var mat = result.Palette.Get(1);
        Assert.NotNull(mat);
        Assert.Equal((byte)0, mat.Color.R);
        Assert.Equal((byte)255, mat.Color.G);
        Assert.Equal((byte)0, mat.Color.B);
    }

    [Fact]
    public void Import_InvalidMagic_Throws()
    {
        var stream = new MemoryStream("NOT VOX!"u8.ToArray());
        var importer = new VoxImporter(LogFactory);

        Assert.Throws<InvalidDataException>(() => importer.Import(stream));
    }

    [Fact]
    public void Export_EmptyModel_ProducesValidFile()
    {
        var model = CreateModel();

        var stream = new MemoryStream();
        new VoxExporter(NullLogger<VoxExporter>.Instance).Export(model, stream);

        stream.Position = 0;
        var result = new VoxImporter(LogFactory).Import(stream);

        Assert.Single(result);
        Assert.Equal(0, result[0].GetVoxelCount());
    }

    [Fact]
    public void Export_LogsLossyWarning()
    {
        var messages = new List<string>();
        var logger = new CapturingLogger<VoxExporter>(messages);

        var model = CreateModel();
        model.SetVoxel(new Point3(0, 0, 0), 1);

        new VoxExporter(logger).Export(model, new MemoryStream());

        Assert.Contains(messages, m => m.Contains("stripped"));
    }

    /// <summary>
    /// Simple logger that captures formatted messages for test assertions.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}
