using VoxelForge.App.Events;

namespace VoxelForge.App.Services;

public readonly record struct SetConfigValueRequest(string Key, string Value, bool Save);

public readonly record struct SetMeasureGridRequest(bool? ShowMeasureGrid, float? VoxelsPerMeter);

public readonly record struct ConfigListEntry(string Key, string Value);

/// <summary>
/// Stateless service for editor configuration queries and updates.
/// </summary>
public sealed class EditorConfigService
{
    private static readonly ConfigKeyBinding[] ConfigKeys =
    [
        new("invertOrbitX", GetInvertOrbitX, SetInvertOrbitX),
        new("invertOrbitY", GetInvertOrbitY, SetInvertOrbitY),
        new("orbitSensitivity", GetOrbitSensitivity, SetOrbitSensitivity),
        new("zoomSensitivity", GetZoomSensitivity, SetZoomSensitivity),
        new("defaultGridHint", GetDefaultGridHint, SetDefaultGridHint),
        new("maxUndoDepth", GetMaxUndoDepth, SetMaxUndoDepth),
        new("maxZoomDistance", GetMaxZoomDistance, SetMaxZoomDistance),
        new("voxelsPerMeter", GetVoxelsPerMeter, SetVoxelsPerMeter),
        new("backgroundColor", GetBackgroundColor, SetBackgroundColor),
        new("showMeasureGrid", GetShowMeasureGrid, SetShowMeasureGrid),
    ];

    public ApplicationServiceResult<IReadOnlyList<ConfigListEntry>> List(EditorConfigState config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var entries = new List<ConfigListEntry>(ConfigKeys.Length);
        for (int i = 0; i < ConfigKeys.Length; i++)
        {
            var binding = ConfigKeys[i];
            entries.Add(new ConfigListEntry(binding.ExternalKey, binding.GetValue(config)));
        }

        return new ApplicationServiceResult<IReadOnlyList<ConfigListEntry>>
        {
            Success = true,
            Message = "Config entries.",
            Data = entries,
        };
    }

    public ApplicationServiceResult Save(EditorConfigState config, IEventPublisher events)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(events);

        config.Save();
        var applicationEvents = new IApplicationEvent[] { new ConfigSavedEvent("config.json") };
        events.PublishAll(applicationEvents);
        return new ApplicationServiceResult
        {
            Success = true,
            Message = "Config saved to config.json",
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult SetValue(
        EditorConfigState config,
        IEventPublisher events,
        SetConfigValueRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(request.Key);
        ArgumentNullException.ThrowIfNull(request.Value);

        var normalizedKey = NormalizeKey(request.Key);
        var binding = FindConfigKey(normalizedKey);
        if (binding is null)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = $"Unknown config key: '{normalizedKey}'",
            };
        }

        var oldValue = binding.GetValue(config);
        var failure = binding.SetValue(config, request.Value);
        if (failure is not null)
        {
            return new ApplicationServiceResult
            {
                Success = false,
                Message = failure,
            };
        }

        if (request.Save)
            config.Save();

        var applicationEvents = new IApplicationEvent[]
        {
            new ConfigChangedEvent(binding.NormalizedKey, oldValue, binding.GetValue(config), request.Save),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"{binding.NormalizedKey} = {request.Value}" + (request.Save ? " (saved)" : string.Empty),
            Events = applicationEvents,
        };
    }

    public ApplicationServiceResult SetMeasureGrid(
        EditorConfigState config,
        IEventPublisher events,
        SetMeasureGridRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(events);

        var applicationEvents = new List<IApplicationEvent>();
        if (request.VoxelsPerMeter.HasValue)
        {
            if (request.VoxelsPerMeter.Value <= 0)
            {
                return new ApplicationServiceResult
                {
                    Success = false,
                    Message = "Voxels per meter must be positive.",
                };
            }

            var oldValue = config.VoxelsPerMeter.ToString();
            config.VoxelsPerMeter = request.VoxelsPerMeter.Value;
            applicationEvents.Add(new ConfigChangedEvent(
                "voxelsPerMeter",
                oldValue,
                config.VoxelsPerMeter.ToString(),
                false));
        }

        if (request.ShowMeasureGrid.HasValue)
        {
            var oldValue = config.ShowMeasureGrid.ToString();
            config.ShowMeasureGrid = request.ShowMeasureGrid.Value;
            applicationEvents.Add(new ConfigChangedEvent(
                "showMeasureGrid",
                oldValue,
                config.ShowMeasureGrid.ToString(),
                false));
        }

        events.PublishAll(applicationEvents);
        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"Measure grid {(config.ShowMeasureGrid ? "ON" : "OFF")} (voxelsPerMeter={config.VoxelsPerMeter})",
            Events = applicationEvents,
        };
    }

    private static ConfigKeyBinding? FindConfigKey(string normalizedKey)
    {
        for (int i = 0; i < ConfigKeys.Length; i++)
        {
            if (ConfigKeys[i].NormalizedKey == normalizedKey)
                return ConfigKeys[i];
        }

        return null;
    }

    private static string GetInvertOrbitX(EditorConfigState config) => config.InvertOrbitX.ToString();

    private static string? SetInvertOrbitX(EditorConfigState config, string value)
    {
        if (!bool.TryParse(value, out var parsed)) return "Expected true/false";
        config.InvertOrbitX = parsed;
        return null;
    }

    private static string GetInvertOrbitY(EditorConfigState config) => config.InvertOrbitY.ToString();

    private static string? SetInvertOrbitY(EditorConfigState config, string value)
    {
        if (!bool.TryParse(value, out var parsed)) return "Expected true/false";
        config.InvertOrbitY = parsed;
        return null;
    }

    private static string GetOrbitSensitivity(EditorConfigState config) => config.OrbitSensitivity.ToString();

    private static string? SetOrbitSensitivity(EditorConfigState config, string value)
    {
        if (!float.TryParse(value, out var parsed)) return "Expected number";
        config.OrbitSensitivity = parsed;
        return null;
    }

    private static string GetZoomSensitivity(EditorConfigState config) => config.ZoomSensitivity.ToString();

    private static string? SetZoomSensitivity(EditorConfigState config, string value)
    {
        if (!float.TryParse(value, out var parsed)) return "Expected number";
        config.ZoomSensitivity = parsed;
        return null;
    }

    private static string GetDefaultGridHint(EditorConfigState config) => config.DefaultGridHint.ToString();

    private static string? SetDefaultGridHint(EditorConfigState config, string value)
    {
        if (!int.TryParse(value, out var parsed)) return "Expected integer";
        config.DefaultGridHint = parsed;
        return null;
    }

    private static string GetMaxUndoDepth(EditorConfigState config) => config.MaxUndoDepth.ToString();

    private static string? SetMaxUndoDepth(EditorConfigState config, string value)
    {
        if (!int.TryParse(value, out var parsed)) return "Expected integer";
        config.MaxUndoDepth = parsed;
        return null;
    }

    private static string GetMaxZoomDistance(EditorConfigState config) => config.MaxZoomDistance.ToString();

    private static string? SetMaxZoomDistance(EditorConfigState config, string value)
    {
        if (!float.TryParse(value, out var parsed)) return "Expected number";
        config.MaxZoomDistance = parsed;
        return null;
    }

    private static string GetVoxelsPerMeter(EditorConfigState config) => config.VoxelsPerMeter.ToString();

    private static string? SetVoxelsPerMeter(EditorConfigState config, string value)
    {
        if (!float.TryParse(value, out var parsed) || parsed <= 0) return "Expected positive number";
        config.VoxelsPerMeter = parsed;
        return null;
    }

    private static string GetBackgroundColor(EditorConfigState config) => string.Join(",", config.BackgroundColor);

    private static string? SetBackgroundColor(EditorConfigState config, string value)
    {
        var parts = value.Split(',');
        if (parts.Length != 3)
            return "Expected R,G,B";

        if (!byte.TryParse(parts[0], out byte r) ||
            !byte.TryParse(parts[1], out byte g) ||
            !byte.TryParse(parts[2], out byte b))
            return "Expected R,G,B values in the 0-255 range";

        config.BackgroundColor = [r, g, b];
        return null;
    }

    private static string GetShowMeasureGrid(EditorConfigState config) => config.ShowMeasureGrid.ToString();

    private static string? SetShowMeasureGrid(EditorConfigState config, string value)
    {
        if (!bool.TryParse(value, out var parsed)) return "Expected true/false";
        config.ShowMeasureGrid = parsed;
        return null;
    }

    private static string NormalizeKey(string key) => key.ToLowerInvariant();

    private sealed class ConfigKeyBinding
    {
        private readonly Func<EditorConfigState, string> _getValue;
        private readonly Func<EditorConfigState, string, string?> _setValue;

        public ConfigKeyBinding(
            string externalKey,
            Func<EditorConfigState, string> getValue,
            Func<EditorConfigState, string, string?> setValue)
        {
            ExternalKey = externalKey;
            NormalizedKey = NormalizeKey(externalKey);
            _getValue = getValue;
            _setValue = setValue;
        }

        public string ExternalKey { get; }
        public string NormalizedKey { get; }

        public string GetValue(EditorConfigState config) => _getValue(config);

        public string? SetValue(EditorConfigState config, string value) => _setValue(config, value);
    }
}
