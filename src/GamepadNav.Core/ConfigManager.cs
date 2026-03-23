using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamepadNav.Core;

/// <summary>
/// Loads, saves, and watches the config file for hot-reload.
/// </summary>
public sealed class ConfigManager : IDisposable
{
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private GamepadNavConfig _config;
    private readonly object _lock = new();

    public GamepadNavConfig Current
    {
        get { lock (_lock) return _config; }
    }

    public event Action<GamepadNavConfig>? ConfigChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public ConfigManager(string? configPath = null)
    {
        _configPath = configPath ?? GamepadNavConfig.ConfigFilePath;
        _config = Load();
        StartWatching();
    }

    public GamepadNavConfig Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaultConfig = new GamepadNavConfig();
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<GamepadNavConfig>(json, JsonOptions) ?? new();
        }
        catch
        {
            return new GamepadNavConfig();
        }
    }

    public void Save(GamepadNavConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);

        lock (_lock) _config = config;
    }

    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(_configPath);
        var file = Path.GetFileName(_configPath);
        if (dir == null || !Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnFileChanged;
    }

    private DateTime _lastReload = DateTime.MinValue;
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: FileSystemWatcher fires multiple events per write
        if ((DateTime.Now - _lastReload).TotalSeconds < 1) return;
        _lastReload = DateTime.Now;

        try
        {
            // Small delay for file write to complete
            Thread.Sleep(200);
            var updated = Load();
            lock (_lock) _config = updated;
            ConfigChanged?.Invoke(updated);
        }
        catch
        {
            // Ignore transient read failures during write
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
