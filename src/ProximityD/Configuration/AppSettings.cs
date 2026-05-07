using System.IO;
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

    /// <summary>Kalman filter process noise parameter. Higher = more responsive to movement, lower = smoother.</summary>
    public double KalmanProcessNoise { get; set; } = 1.0;

    /// <summary>Kalman filter measurement noise parameter. Higher = more smoothing of jitter, lower = more responsive.</summary>
    public double KalmanMeasurementNoise { get; set; } = 4.0;

    /// <summary>List of tracked device IDs.</summary>
    public List<TrackedDeviceConfig> TrackedDevices { get; set; } = new();

    /// <summary>Signal filter type to use.</summary>
    public SignalFilterType FilterType { get; set; } = SignalFilterType.Kalman;

    /// <summary>Moving average window size (if using moving average filter).</summary>
    public int MovingAverageWindowSize { get; set; } = 10;

    /// <summary>Whether to show notifications.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Bluetooth TX power (RSSI at 1 meter) for distance calculation.</summary>
    public int TxPowerDbm { get; set; } = -59;

    /// <summary>Environment path loss exponent. 2.0 = free space, 3.0 = indoor with obstacles.</summary>
    public double PathLossExponent { get; set; } = 2.0;

    /// <summary>Show estimated distance in meters instead of raw RSSI.</summary>
    public bool EnableDistanceMode { get; set; } = false;

    /// <summary>Enable WiFi-based presence detection as a secondary signal.</summary>
    public bool EnableWifiPresence { get; set; } = false;

    /// <summary>Hostname or IP address to ping for WiFi presence detection.</summary>
    public string WifiDeviceHostname { get; set; } = string.Empty;

    /// <summary>How often to ping the device for WiFi presence (seconds).</summary>
    public int WifiPingIntervalSeconds { get; set; } = 10;

    /// <summary>Show a toast notification when the device returns to encourage authentication.</summary>
    public bool EnableWindowsHelloNotification { get; set; } = true;

    /// <summary>How long to display the notification (seconds).</summary>
    public int NotificationTimeoutSeconds { get; set; } = 10;

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

    /// <summary>
    /// When true, the scanner treats the strongest currently-nearby BLE
    /// advertisement as a reading for this device, ignoring address mismatches.
    /// Required for iOS phones, which broadcast Random Resolvable Private
    /// Addresses (RPAs) that rotate every ~15 minutes and never match the
    /// stored identity address from pairing. Default true.
    /// </summary>
    public bool AssumePrivacyMode { get; set; } = true;
}

public enum SignalFilterType
{
    Kalman,
    MovingAverage,
    None
}
