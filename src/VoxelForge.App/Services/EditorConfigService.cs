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
    public ApplicationServiceResult<IReadOnlyList<ConfigListEntry>> List(EditorConfigState config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var entries = new List<ConfigListEntry>
        {
            new("invertOrbitX", config.InvertOrbitX.ToString()),
            new("invertOrbitY", config.InvertOrbitY.ToString()),
            new("orbitSensitivity", config.OrbitSensitivity.ToString()),
            new("zoomSensitivity", config.ZoomSensitivity.ToString()),
            new("defaultGridHint", config.DefaultGridHint.ToString()),
            new("maxUndoDepth", config.MaxUndoDepth.ToString()),
            new("maxZoomDistance", config.MaxZoomDistance.ToString()),
            new("voxelsPerMeter", config.VoxelsPerMeter.ToString()),
            new("backgroundColor", string.Join(",", config.BackgroundColor)),
            new("showMeasureGrid", config.ShowMeasureGrid.ToString()),
        };

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

        var key = NormalizeKey(request.Key);
        var oldValue = GetConfigValue(config, key);
        var failure = SetConfigValue(config, key, request.Value);
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
            new ConfigChangedEvent(key, oldValue, GetConfigValue(config, key), request.Save),
        };
        events.PublishAll(applicationEvents);

        return new ApplicationServiceResult
        {
            Success = true,
            Message = $"{key} = {request.Value}" + (request.Save ? " (saved)" : string.Empty),
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

    private static string? SetConfigValue(EditorConfigState config, string key, string value)
    {
        switch (key)
        {
            case "invertorbitx":
                if (!bool.TryParse(value, out var ix)) return "Expected true/false";
                config.InvertOrbitX = ix;
                return null;
            case "invertorbity":
                if (!bool.TryParse(value, out var iy)) return "Expected true/false";
                config.InvertOrbitY = iy;
                return null;
            case "orbitsensitivity":
                if (!float.TryParse(value, out var os)) return "Expected number";
                config.OrbitSensitivity = os;
                return null;
            case "zoomsensitivity":
                if (!float.TryParse(value, out var zs)) return "Expected number";
                config.ZoomSensitivity = zs;
                return null;
            case "defaultgridhint":
                if (!int.TryParse(value, out var dg)) return "Expected integer";
                config.DefaultGridHint = dg;
                return null;
            case "maxundodepth":
                if (!int.TryParse(value, out var mu)) return "Expected integer";
                config.MaxUndoDepth = mu;
                return null;
            case "maxzoomdistance":
                if (!float.TryParse(value, out var mz)) return "Expected number";
                config.MaxZoomDistance = mz;
                return null;
            case "voxelspermeter":
                if (!float.TryParse(value, out var vpm) || vpm <= 0) return "Expected positive number";
                config.VoxelsPerMeter = vpm;
                return null;
            case "backgroundcolor":
                return SetBackgroundColor(config, value);
            default:
                return $"Unknown config key: '{key}'";
        }
    }

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

    private static string? GetConfigValue(EditorConfigState config, string key) => key switch
    {
        "invertorbitx" => config.InvertOrbitX.ToString(),
        "invertorbity" => config.InvertOrbitY.ToString(),
        "orbitsensitivity" => config.OrbitSensitivity.ToString(),
        "zoomsensitivity" => config.ZoomSensitivity.ToString(),
        "defaultgridhint" => config.DefaultGridHint.ToString(),
        "maxundodepth" => config.MaxUndoDepth.ToString(),
        "maxzoomdistance" => config.MaxZoomDistance.ToString(),
        "voxelspermeter" => config.VoxelsPerMeter.ToString(),
        "backgroundcolor" => string.Join(",", config.BackgroundColor),
        _ => null,
    };

    private static string NormalizeKey(string key) => key.ToLowerInvariant();
}
