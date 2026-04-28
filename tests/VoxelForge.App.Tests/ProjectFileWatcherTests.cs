using Microsoft.Extensions.Logging.Abstractions;
using VoxelForge.App;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.LivePreview;
using VoxelForge.App.Services;
using VoxelForge.Core;
using VoxelForge.Core.Serialization;

namespace VoxelForge.App.Tests;

public sealed class ProjectFileWatcherTests
{
    [Fact]
    public void Tick_LoadsExistingWatchedProjectAfterStart()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "preview.vforge");
        WriteProject(path, new Point3(1, 2, 3), 4);

        try
        {
            var document = CreateEmptyDocument();
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);
            using var service = CreateService(document, undoStack, events, path);

            service.Start();
            var result = service.Tick(DateTimeOffset.UtcNow);

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal((byte)4, document.Model.GetVoxel(new Point3(1, 2, 3)));
            Assert.Equal(1, service.SuccessfulLoadCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tick_ReloadsAfterExplicitChangeRequest()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "preview.vforge");
        WriteProject(path, new Point3(0, 0, 0), 1);

        try
        {
            var document = CreateEmptyDocument();
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);
            using var service = CreateService(document, undoStack, events, path);

            service.Start();
            var initial = service.Tick(DateTimeOffset.UtcNow);
            Assert.NotNull(initial);
            Assert.True(initial.Success);

            WriteProject(path, new Point3(5, 0, 0), 2);
            service.RequestReload();
            var reloaded = service.Tick(DateTimeOffset.UtcNow);

            Assert.NotNull(reloaded);
            Assert.True(reloaded.Success);
            Assert.Null(document.Model.GetVoxel(new Point3(0, 0, 0)));
            Assert.Equal((byte)2, document.Model.GetVoxel(new Point3(5, 0, 0)));
            Assert.Equal(2, service.SuccessfulLoadCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tick_ReportsInvalidProjectWithoutReplacingCurrentDocument()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "preview.vforge");
        WriteProject(path, new Point3(0, 0, 0), 1);

        try
        {
            var document = CreateEmptyDocument();
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);
            using var service = CreateService(document, undoStack, events, path);

            service.Start();
            var initial = service.Tick(DateTimeOffset.UtcNow);
            Assert.NotNull(initial);
            Assert.True(initial.Success);

            File.WriteAllText(path, "{ invalid json");
            service.RequestReload();
            var failed = service.Tick(DateTimeOffset.UtcNow);

            Assert.NotNull(failed);
            Assert.False(failed.Success);
            Assert.Contains("Failed to reload watched project file", failed.Message, StringComparison.Ordinal);
            Assert.Equal((byte)1, document.Model.GetVoxel(new Point3(0, 0, 0)));
            Assert.Equal(1, service.SuccessfulLoadCount);
            Assert.Contains("Failed to reload watched project file", service.LastStatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Tick_RetriesAfterTransientInvalidProjectBecomesValid()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "preview.vforge");
        WriteProject(path, new Point3(0, 0, 0), 1);

        try
        {
            var document = CreateEmptyDocument();
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);
            using var service = CreateService(document, undoStack, events, path);

            service.Start();
            var initial = service.Tick(DateTimeOffset.UtcNow);
            Assert.NotNull(initial);
            Assert.True(initial.Success);

            File.WriteAllText(path, "{ invalid json");
            service.RequestReload();
            var failed = service.Tick(DateTimeOffset.UtcNow);
            Assert.NotNull(failed);
            Assert.False(failed.Success);

            WriteProject(path, new Point3(9, 0, 0), 2);
            var retried = service.Tick(DateTimeOffset.UtcNow);

            Assert.NotNull(retried);
            Assert.True(retried.Success);
            Assert.Null(document.Model.GetVoxel(new Point3(0, 0, 0)));
            Assert.Equal((byte)2, document.Model.GetVoxel(new Point3(9, 0, 0)));
            Assert.Equal(2, service.SuccessfulLoadCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Tick_ReloadsAfterOsCreateOrRenameEvent()
    {
        string directory = CreateTempDirectory();
        string path = Path.Combine(directory, "preview.vforge");
        string tempPath = Path.Combine(directory, "preview.tmp");

        try
        {
            var document = CreateEmptyDocument();
            var events = new ApplicationEventDispatcher();
            var undoStack = CreateUndoStack(events);
            using var service = CreateService(document, undoStack, events, path, TimeSpan.FromMilliseconds(10));

            service.Start();
            Assert.Contains("Waiting for watched project file", service.LastStatusMessage, StringComparison.Ordinal);

            WriteProject(tempPath, new Point3(7, 8, 9), 3);
            File.Move(tempPath, path);

            var result = await WaitForSuccessfulReloadAsync(service, TimeSpan.FromSeconds(2));

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal((byte)3, document.Model.GetVoxel(new Point3(7, 8, 9)));
            Assert.Equal(1, service.SuccessfulLoadCount);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<ApplicationServiceResult?> WaitForSuccessfulReloadAsync(ProjectFileWatcher service, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        ApplicationServiceResult? lastResult = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastResult = service.Tick(DateTimeOffset.UtcNow);
            if (lastResult?.Success == true)
                return lastResult;

            await Task.Delay(20);
        }

        return lastResult;
    }

    private static ProjectFileWatcher CreateService(
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        string path,
        TimeSpan? debounce = null)
    {
        return new ProjectFileWatcher(
            new ProjectLifecycleService(NullLoggerFactory.Instance),
            document,
            undoStack,
            events,
            path,
            NullLogger<ProjectFileWatcher>.Instance,
            debounce ?? TimeSpan.Zero);
    }

    private static void WriteProject(string path, Point3 position, byte paletteIndex)
    {
        var model = new VoxelModel(NullLogger<VoxelModel>.Instance) { GridHint = 32 };
        model.Palette.Set(paletteIndex, new MaterialDef
        {
            Name = "Preview",
            Color = new RgbaColor(20, 30, 40),
        });
        model.SetVoxel(position, paletteIndex);
        var labels = new LabelIndex(NullLogger<LabelIndex>.Instance);
        var serializer = new ProjectSerializer(NullLoggerFactory.Instance);
        string json = serializer.Serialize(model, labels, [], new ProjectMetadata { Name = "preview" });
        File.WriteAllText(path, json);
    }

    private static EditorDocumentState CreateEmptyDocument()
    {
        return new EditorDocumentState(
            new VoxelModel(NullLogger<VoxelModel>.Instance),
            new LabelIndex(NullLogger<LabelIndex>.Instance));
    }

    private static UndoStack CreateUndoStack(IEventPublisher events) =>
        new(new UndoHistoryState(100), NullLogger<UndoStack>.Instance, events);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "voxelforge-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
