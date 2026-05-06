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
        if (_isScanning)
        {
            return;
        }

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
        if (!_isScanning)
        {
            return;
        }

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
        _scanCts?.Dispose();
        _scanCts = null;
        _isScanning = false;
        _logger.LogInformation("BLE scanning stopped");
    }

    /// <summary>
    /// Discover available BLE devices for pairing/tracking.
    /// Returns paired devices and any nearby unpaired devices observed via active
    /// advertisement scanning during the discovery window.
    /// Uses the Bluetooth address as the device identifier for consistency with advertisement tracking.
    /// </summary>
    public async Task<List<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

#if WINDOWS
        // 1. Enumerate paired BLE devices
        try
        {
            var deviceSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var pairedDevices = await DeviceInformation.FindAllAsync(deviceSelector);

            foreach (var device in pairedDevices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Resolve BluetoothAddress for consistent identification with advertisement watcher
                string bluetoothAddress;
                try
                {
                    using var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
                    bluetoothAddress = bleDevice != null
                        ? bleDevice.BluetoothAddress.ToString("X12")
                        : device.Id;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve BluetoothAddress for {DeviceId}", device.Id);
                    bluetoothAddress = device.Id;
                }

                devices[bluetoothAddress] = new DiscoveredDevice
                {
                    DeviceId = bluetoothAddress,
                    DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name,
                    IsPaired = true
                };
            }

            _logger.LogInformation("Found {Count} paired BLE devices", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate paired BLE devices. Ensure Bluetooth is enabled.");
            throw;
        }

        // 2. Briefly listen for nearby advertisements to surface unpaired devices.
        try
        {
            await ScanForNearbyAdvertisementsAsync(devices, TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Active advertisement scan failed");
        }
#else
        // Simulated devices for testing
        await Task.Delay(1000, cancellationToken);
        devices["AABBCCDDEEFF"] = new DiscoveredDevice { DeviceId = "AABBCCDDEEFF", DeviceName = "Simulated Phone", IsPaired = true };
        devices["112233445566"] = new DiscoveredDevice { DeviceId = "112233445566", DeviceName = "Simulated Watch", IsPaired = true };
#endif

        return devices.Values.ToList();
    }

#if WINDOWS
    private Task ScanForNearbyAdvertisementsAsync(
        Dictionary<string, DiscoveredDevice> devices,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            SignalStrengthFilter =
            {
                InRangeThresholdInDBm = -100,
                OutOfRangeThresholdInDBm = -105,
                OutOfRangeTimeout = TimeSpan.FromSeconds(5)
            }
        };

        void OnReceived(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var address = args.BluetoothAddress.ToString("X12");
            var name = args.Advertisement.LocalName;
            lock (devices)
            {
                if (devices.TryGetValue(address, out var existing))
                {
                    if (string.IsNullOrWhiteSpace(existing.DeviceName) || existing.DeviceName == "Unknown")
                    {
                        existing.DeviceName = string.IsNullOrWhiteSpace(name) ? existing.DeviceName : name;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(name))
                {
                    devices[address] = new DiscoveredDevice
                    {
                        DeviceId = address,
                        DeviceName = name,
                        IsPaired = false
                    };
                }
            }
        }

        watcher.Received += OnReceived;
        watcher.Start();
        _logger.LogInformation("Active BLE discovery started ({Seconds}s)", duration.TotalSeconds);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);
        cts.Token.Register(() =>
        {
            try
            {
                watcher.Stop();
                watcher.Received -= OnReceived;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping discovery watcher");
            }
            finally
            {
                cts.Dispose();
                tcs.TrySetResult(true);
            }
        });

        return tcs.Task;
    }
#endif

#if WINDOWS
    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var deviceId = args.BluetoothAddress.ToString("X12");
        var rssi = args.RawSignalStrengthInDBm;
        var localName = args.Advertisement.LocalName;

        // Check if this is a tracked device (match on Bluetooth address)
        var trackedDevice = _settings.TrackedDevices.FirstOrDefault(d =>
            d.Enabled && (d.DeviceId == deviceId || d.MacAddress == deviceId));

        if (trackedDevice != null)
        {
            DeviceDetected?.Invoke(this, new BleDeviceReading
            {
                DeviceId = deviceId,
                DeviceName = !string.IsNullOrWhiteSpace(localName) ? localName : trackedDevice.DeviceName,
                Rssi = rssi,
                Timestamp = DateTime.UtcNow
            });
        }
        else if (!string.IsNullOrWhiteSpace(localName))
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

                try
                {
                    await Task.Delay(_settings.ScanIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ct);
    }

    public void Dispose()
    {
        StopScanning();
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
