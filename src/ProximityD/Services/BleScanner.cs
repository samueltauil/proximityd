using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Models;

#if WINDOWS
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
#endif

namespace ProximityD.Services;

/// <summary>
/// Bluetooth Low Energy scanner using WinRT APIs.
/// Continuously scans for BLE advertisements and reports RSSI values.
/// </summary>
public class BleScanner : IDisposable
{
    private readonly ILogger<BleScanner> _logger;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _scanCts;
    private bool _isScanning;

#if WINDOWS
    private BluetoothLEAdvertisementWatcher? _watcher;
#endif

    /// <summary>
    /// Fired when a tracked device advertisement is received with RSSI.
    /// </summary>
    public event EventHandler<BleDeviceReading>? DeviceDetected;

    /// <summary>
    /// Fired when a new (untracked) device is discovered during scanning.
    /// </summary>
    public event EventHandler<DiscoveredDevice>? DeviceDiscovered;

    public bool IsScanning => _isScanning;

    public BleScanner(ILogger<BleScanner> logger, AppSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>
    /// Start scanning for BLE device advertisements.
    /// </summary>
    public void StartScanning()
    {
        if (_isScanning) return;

#if WINDOWS
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter =
            {
                // Only report devices with RSSI above minimum threshold
                InRangeThresholdInDBm = -100,
                OutOfRangeThresholdInDBm = -105,
                OutOfRangeTimeout = TimeSpan.FromSeconds(5)
            }
        };

        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;
        _watcher.Start();

        _isScanning = true;
        _logger.LogInformation("BLE scanning started");
#else
        _logger.LogWarning("BLE scanning is only supported on Windows");
        // Simulate scanning for development/testing on non-Windows
        StartSimulatedScanning();
#endif
    }

    /// <summary>
    /// Stop scanning.
    /// </summary>
    public void StopScanning()
    {
        if (!_isScanning) return;

#if WINDOWS
        _watcher?.Stop();
        if (_watcher != null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
        }
        _watcher = null;
#endif

        _scanCts?.Cancel();
        _isScanning = false;
        _logger.LogInformation("BLE scanning stopped");
    }

    /// <summary>
    /// Discover available BLE devices for pairing/tracking.
    /// </summary>
    public async Task<List<DiscoveredDevice>> DiscoverDevicesAsync(TimeSpan timeout)
    {
        var devices = new List<DiscoveredDevice>();

#if WINDOWS
        var deviceSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var pairedDevices = await DeviceInformation.FindAllAsync(deviceSelector);

        foreach (var device in pairedDevices)
        {
            devices.Add(new DiscoveredDevice
            {
                DeviceId = device.Id,
                DeviceName = device.Name ?? "Unknown",
                IsPaired = true
            });
        }

        _logger.LogInformation("Found {Count} paired BLE devices", devices.Count);
#else
        // Simulated devices for testing
        await Task.Delay(1000);
        devices.Add(new DiscoveredDevice { DeviceId = "sim-001", DeviceName = "Simulated Phone", IsPaired = true });
        devices.Add(new DiscoveredDevice { DeviceId = "sim-002", DeviceName = "Simulated Watch", IsPaired = true });
#endif

        return devices;
    }

#if WINDOWS
    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var deviceId = args.BluetoothAddress.ToString("X12");
        var rssi = args.RawSignalStrengthInDBm;
        var localName = args.Advertisement.LocalName;

        // Check if this is a tracked device
        var isTracked = _settings.TrackedDevices.Any(d =>
            d.Enabled && (d.DeviceId == deviceId || d.MacAddress == deviceId));

        if (isTracked)
        {
            DeviceDetected?.Invoke(this, new BleDeviceReading
            {
                DeviceId = deviceId,
                DeviceName = localName ?? deviceId,
                Rssi = rssi,
                Timestamp = DateTime.UtcNow
            });
        }
        else if (!string.IsNullOrEmpty(localName))
        {
            DeviceDiscovered?.Invoke(this, new DiscoveredDevice
            {
                DeviceId = deviceId,
                DeviceName = localName,
                IsPaired = false
            });
        }
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        _logger.LogWarning("BLE watcher stopped: {Status}", args.Error);
        _isScanning = false;
    }
#endif

    private void StartSimulatedScanning()
    {
        _isScanning = true;
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        Task.Run(async () =>
        {
            var random = new Random();
            while (!ct.IsCancellationRequested)
            {
                foreach (var device in _settings.TrackedDevices.Where(d => d.Enabled))
                {
                    // Simulate RSSI with noise
                    var baseRssi = -60; // Simulate device being in range
                    var noise = random.Next(-10, 10);
                    var rssi = (short)(baseRssi + noise);

                    DeviceDetected?.Invoke(this, new BleDeviceReading
                    {
                        DeviceId = device.DeviceId,
                        DeviceName = device.DeviceName,
                        Rssi = rssi,
                        Timestamp = DateTime.UtcNow
                    });
                }

                await Task.Delay(_settings.ScanIntervalMs, ct);
            }
        }, ct);
    }

    public void Dispose()
    {
        StopScanning();
        _scanCts?.Dispose();
    }
}

/// <summary>
/// A raw RSSI reading from a BLE device.
/// </summary>
public class BleDeviceReading
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public short Rssi { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// A discovered BLE device.
/// </summary>
public class DiscoveredDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public bool IsPaired { get; set; }
}
