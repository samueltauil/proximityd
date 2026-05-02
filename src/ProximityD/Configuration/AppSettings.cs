using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProximityD.Configuration;

/// <summary>
/// Application settings for ProximityD.
/// </summary>
public class AppSettings
{
    /// <summary>File path for persistent settings storage.</summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProximityD", "settings.json");

    /// <summary>Bluetooth scanning interval in milliseconds.</summary>
    public int ScanIntervalMs { get; set; } = 2000;

    /// <summary>RSSI threshold to trigger lock (device is far). More negative = farther.</summary>
    public int LockRssiThreshold { get; set; } = -75;

    /// <summary>RSSI threshold to trigger unlock (device is near). Less negative = closer.</summary>
    public int UnlockRssiThreshold { get; set; } = -65;

    /// <summary>Duration in seconds the signal must be below lock threshold before locking.</summary>
    public int LockDelaySeconds { get; set; } = 10;

    /// <summary>Duration in seconds the signal must be above unlock threshold before unlocking.</summary>
    public int UnlockDelaySeconds { get; set; } = 5;

    /// <summary>Duration in seconds before device is considered lost (no signal at all).</summary>
    public int DeviceLostTimeoutSeconds { get; set; } = 30;

    /// <summary>Whether to auto-lock when device goes out of range.</summary>
    public bool EnableAutoLock { get; set; } = true;

    /// <summary>Whether to auto-unlock when device returns. Disabled by default for security.</summary>
    public bool EnableAutoUnlock { get; set; } = false;

    /// <summary>Whether to start with Windows.</summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>Whether to start minimized to tray.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>Kalman filter process noise parameter.</summary>
    public double KalmanProcessNoise { get; set; } = 0.1;

    /// <summary>Kalman filter measurement noise parameter.</summary>
    public double KalmanMeasurementNoise { get; set; } = 10.0;

    /// <summary>List of tracked device IDs.</summary>
    public List<TrackedDeviceConfig> TrackedDevices { get; set; } = new();

    /// <summary>Signal filter type to use.</summary>
    public SignalFilterType FilterType { get; set; } = SignalFilterType.Kalman;

    /// <summary>Moving average window size (if using moving average filter).</summary>
    public int MovingAverageWindowSize { get; set; } = 10;

    /// <summary>Whether to show notifications.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Log level for file logging.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Load settings from disk.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // If settings are corrupted, return defaults
        }
        return new AppSettings();
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public class TrackedDeviceConfig
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public enum SignalFilterType
{
    Kalman,
    MovingAverage,
    None
}
