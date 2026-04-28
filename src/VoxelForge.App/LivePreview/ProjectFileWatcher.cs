using System.Text.Json;
using Microsoft.Extensions.Logging;
using VoxelForge.App.Commands;
using VoxelForge.App.Events;
using VoxelForge.App.Services;

namespace VoxelForge.App.LivePreview;

/// <summary>
/// Watches one project file and reloads it through the normal project lifecycle path
/// from the caller's update loop after a debounce interval.
/// </summary>
public sealed class ProjectFileWatcher : IDisposable
{
    private const int DefaultRetryCount = 3;
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly object _syncRoot = new();
    private readonly ProjectLifecycleService _projectLifecycleService;
    private readonly EditorDocumentState _document;
    private readonly UndoStack _undoStack;
    private readonly IEventPublisher _events;
    private readonly ILogger<ProjectFileWatcher> _logger;
    private readonly TimeSpan _debounce;
    private readonly string _directory;
    private readonly string _fileName;
    private FileSystemWatcher? _watcher;
    private DateTimeOffset? _reloadDueAtUtc;
    private string? _lastStatusMessage;
    private int _remainingRetries;
    private int _successfulLoadCount;
    private bool _disposed;

    public ProjectFileWatcher(
        ProjectLifecycleService projectLifecycleService,
        EditorDocumentState document,
        UndoStack undoStack,
        IEventPublisher events,
        string path,
        ILogger<ProjectFileWatcher> logger,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(projectLifecycleService);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(undoStack);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        WatchedPath = Path.GetFullPath(path);
        _directory = Path.GetDirectoryName(WatchedPath) ?? Directory.GetCurrentDirectory();
        _fileName = Path.GetFileName(WatchedPath);
        if (string.IsNullOrWhiteSpace(_fileName))
            throw new ArgumentException("Watch path must include a file name.", nameof(path));

        _projectLifecycleService = projectLifecycleService;
        _document = document;
        _undoStack = undoStack;
        _events = events;
        _logger = logger;
        _debounce = debounce ?? DefaultDebounce;
    }

    public string WatchedPath { get; }

    public string? LastStatusMessage
    {
        get
        {
            lock (_syncRoot)
                return _lastStatusMessage;
        }
    }

    public int SuccessfulLoadCount
    {
        get
        {
            lock (_syncRoot)
                return _successfulLoadCount;
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_watcher is not null)
            return;

        Directory.CreateDirectory(_directory);
        _watcher = new FileSystemWatcher(_directory, _fileName)
        {
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnWatchedFileChanged;
        _watcher.Created += OnWatchedFileChanged;
        _watcher.Renamed += OnWatchedFileRenamed;
        _watcher.Deleted += OnWatchedFileDeleted;

        if (File.Exists(WatchedPath))
        {
            RequestReload(TimeSpan.Zero);
        }
        else
        {
            SetLastStatusMessage($"Waiting for watched project file: {WatchedPath}");
            _logger.LogInformation("Waiting for watched project file: {Path}", WatchedPath);
        }
    }

    public void RequestReload()
    {
        RequestReload(_debounce);
    }

    public ApplicationServiceResult? Tick(DateTimeOffset nowUtc)
    {
        ThrowIfDisposed();
        bool shouldReload;
        lock (_syncRoot)
        {
            shouldReload = _reloadDueAtUtc is not null && _reloadDueAtUtc.Value <= nowUtc;
            if (shouldReload)
                _reloadDueAtUtc = null;
        }

        if (!shouldReload)
            return null;

        return TryLoad(nowUtc);
    }

    private void RequestReload(TimeSpan delay)
    {
        lock (_syncRoot)
        {
            _reloadDueAtUtc = DateTimeOffset.UtcNow + delay;
            _remainingRetries = DefaultRetryCount;
        }
    }

    private ApplicationServiceResult TryLoad(DateTimeOffset nowUtc)
    {
        if (!File.Exists(WatchedPath))
        {
            string missingMessage = $"Watched project file does not exist yet: {WatchedPath}";
            SetLastStatusMessage(missingMessage);
            return new ApplicationServiceResult
            {
                Success = false,
                Message = missingMessage,
            };
        }

        try
        {
            var result = _projectLifecycleService.Load(
                _document,
                _undoStack,
                _events,
                new LoadProjectRequest(WatchedPath));
            SetLastStatusMessage(result.Message);
            if (result.Success)
            {
                IncrementSuccessfulLoadCount();
                _logger.LogInformation("Reloaded watched project file {Path}: {Message}", WatchedPath, result.Message);
            }
            else
            {
                _logger.LogWarning("Failed to reload watched project file {Path}: {Message}", WatchedPath, result.Message);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException
            or NotSupportedException)
        {
            string message = $"Failed to reload watched project file '{WatchedPath}': {ex.Message}";
            SetLastStatusMessage(message);
            _logger.LogWarning(ex, "Failed to reload watched project file {Path}", WatchedPath);
            ScheduleRetryIfAvailable(nowUtc);
            return new ApplicationServiceResult
            {
                Success = false,
                Message = message,
            };
        }
    }

    private void ScheduleRetryIfAvailable(DateTimeOffset nowUtc)
    {
        lock (_syncRoot)
        {
            if (_remainingRetries <= 0)
                return;

            _remainingRetries--;
            _reloadDueAtUtc = nowUtc + _debounce;
        }
    }

    private void SetLastStatusMessage(string message)
    {
        lock (_syncRoot)
            _lastStatusMessage = message;
    }

    private void IncrementSuccessfulLoadCount()
    {
        lock (_syncRoot)
            _successfulLoadCount++;
    }

    private void OnWatchedFileChanged(object sender, FileSystemEventArgs e)
    {
        RequestReload();
    }

    private void OnWatchedFileRenamed(object sender, RenamedEventArgs e)
    {
        RequestReload();
    }

    private void OnWatchedFileDeleted(object sender, FileSystemEventArgs e)
    {
        SetLastStatusMessage($"Watched project file was deleted: {WatchedPath}");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnWatchedFileChanged;
            _watcher.Created -= OnWatchedFileChanged;
            _watcher.Renamed -= OnWatchedFileRenamed;
            _watcher.Deleted -= OnWatchedFileDeleted;
            _watcher.Dispose();
        }

        _disposed = true;
    }
}
