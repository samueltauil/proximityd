using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Models;

#if WINDOWS
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
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
    private volatile bool _isScanning;

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

    /// <summary>
    /// Fired for every advertisement received while <see cref="IsCalibrating"/> is true.
    /// Bypasses tracked/enabled gating so the calibration wizard can collect samples for
    /// any device the user picks, regardless of whether it is in the tracked list yet.
    /// </summary>
    public event EventHandler<BleDeviceReading>? CalibrationReadingReceived;

    /// <summary>
    /// When true, every received advertisement is also forwarded as a
    /// <see cref="CalibrationReadingReceived"/> event, regardless of whether the
    /// device is tracked. The wizard buckets samples by address itself so iOS
    /// rotating private addresses are handled transparently.
    /// </summary>
    public bool IsCalibrating { get; set; }

    /// <summary>
    /// Optional async callback used during pairing when the remote device requires a PIN
    /// to be entered on this PC (DevicePairingKinds.ProvidePin). The implementation should
    /// prompt the user and return the PIN they read off their phone, or null/empty to cancel.
    /// Some Android phones use this ceremony.
    /// </summary>
    public Func<string, Task<string?>>? PinRequested { get; set; }

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
        // NOTE: do NOT set SignalStrengthFilter thresholds here. Configuring
        // InRange/OutOfRange thresholds puts the watcher into "filtered" mode where
        // Received fires only on the first packet per device per range transition,
        // which causes us to miss SCAN_RSP packets (where iPhones publish their name).
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
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
    /// Fast enumeration of currently paired Bluetooth devices (BLE + classic) without
    /// performing any advertisement scan. Suitable for refreshing the "Paired" column
    /// at app startup.
    /// </summary>
    public async Task<List<DiscoveredDevice>> GetPairedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);

#if WINDOWS
        try
        {
            var bleSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var pairedBle = await DeviceInformation.FindAllAsync(bleSelector);
            foreach (var device in pairedBle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string addr;
                try
                {
                    using var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
                    addr = bleDevice != null ? bleDevice.BluetoothAddress.ToString("X12") : device.Id;
                }
                catch
                {
                    addr = device.Id;
                }

                devices[addr] = new DiscoveredDevice
                {
                    DeviceId = addr,
                    DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name,
                    IsPaired = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate paired BLE devices at startup");
        }

        try
        {
            var classicSelector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var pairedClassic = await DeviceInformation.FindAllAsync(classicSelector);
            foreach (var device in pairedClassic)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string addr;
                try
                {
                    using var classic = await Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(device.Id);
                    addr = classic != null ? classic.BluetoothAddress.ToString("X12") : device.Id;
                }
                catch
                {
                    addr = device.Id;
                }

                if (devices.TryGetValue(addr, out var existing))
                {
                    existing.IsPaired = true;
                    if (string.IsNullOrWhiteSpace(existing.DeviceName) || existing.DeviceName == "Unknown")
                    {
                        existing.DeviceName = device.Name ?? existing.DeviceName;
                    }
                }
                else
                {
                    devices[addr] = new DiscoveredDevice
                    {
                        DeviceId = addr,
                        DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name,
                        IsPaired = true
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate paired classic devices at startup");
        }
#else
        await Task.CompletedTask;
#endif

        return devices.Values.ToList();
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

        // 1b. Enumerate paired Bluetooth Classic (BR/EDR) devices. iPhones pair over classic
        //     Bluetooth, so they only show up here, not in the BLE-paired enumeration above.
        try
        {
            var classicSelector = Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var pairedClassic = await DeviceInformation.FindAllAsync(classicSelector);

            foreach (var device in pairedClassic)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string addr;
                try
                {
                    using var classic = await Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(device.Id);
                    addr = classic != null
                        ? classic.BluetoothAddress.ToString("X12")
                        : device.Id;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to resolve classic address for {DeviceId}", device.Id);
                    addr = device.Id;
                }

                if (devices.TryGetValue(addr, out var existing))
                {
                    existing.IsPaired = true;
                    if (string.IsNullOrWhiteSpace(existing.DeviceName) || existing.DeviceName == "Unknown")
                    {
                        existing.DeviceName = device.Name ?? existing.DeviceName;
                    }
                }
                else
                {
                    devices[addr] = new DiscoveredDevice
                    {
                        DeviceId = addr,
                        DeviceName = string.IsNullOrWhiteSpace(device.Name) ? "Unknown" : device.Name,
                        IsPaired = true
                    };
                }
            }

            _logger.LogInformation("Total paired devices (BLE + classic): {Count}", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate paired classic Bluetooth devices");
        }

        // 2. Briefly listen for nearby advertisements to surface unpaired devices.
        try
        {
            await ScanForNearbyAdvertisementsAsync(devices, TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Active advertisement scan failed");
        }

        // 2b. Bluetooth Classic (BR/EDR) inquiry — phones in Settings > Bluetooth become
        //     discoverable on classic Bluetooth and broadcast their real device name there
        //     (iPhones in particular suppress LocalName on BLE adverts entirely). Match
        //     classic results back onto the BLE address dictionary so the iPhone shows up
        //     with its proper name.
        try
        {
            await ScanForClassicBluetoothNamesAsync(devices, TimeSpan.FromSeconds(12), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classic Bluetooth inquiry failed");
        }

        // 3. Best-effort name resolution for devices we still only have a generic label for
        //    (typical for iPhones, which omit LocalName from advertisements). Windows often
        //    has a cached friendly name available via BluetoothLEDevice.Name, or can fetch it
        //    through the GAP service on a brief connect.
        try
        {
            await ResolveDeviceNamesAsync(devices, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Name resolution pass failed");
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

        // No SignalStrengthFilter — we need every packet (incl. SCAN_RSP) so we can
        // pick up the iPhone's name when its Bluetooth settings screen is open.
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        void OnReceived(BluetoothLEAdvertisementWatcher s, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            var address = args.BluetoothAddress.ToString("X12");
            var name = ExtractAdvertisementName(args.Advertisement);
            var rssi = args.RawSignalStrengthInDBm;
            // Many phones (notably iPhones) suppress LocalName in the primary advert and only
            // include it in the SCAN_RSP packet when their Bluetooth settings screen is open.
            // Fall back to a manufacturer-derived label until a real name arrives.
            var displayName = !string.IsNullOrWhiteSpace(name)
                ? name
                : InferNameFromManufacturerData(args.Advertisement);
            lock (devices)
            {
                if (devices.TryGetValue(address, out var existing))
                {
                    // Always prefer a real (non-placeholder) name once we get one, even if we
                    // had a placeholder previously.
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        existing.DeviceName = name!;
                    }
                    else if (IsPlaceholderName(existing.DeviceName) && !string.IsNullOrWhiteSpace(displayName))
                    {
                        existing.DeviceName = displayName;
                    }
                    if (rssi > existing.Rssi || existing.Rssi == 0)
                    {
                        existing.Rssi = rssi;
                    }
                    existing.AdvertisementCount++;
                }
                else
                {
                    devices[address] = new DiscoveredDevice
                    {
                        DeviceId = address,
                        DeviceName = string.IsNullOrWhiteSpace(displayName) ? $"Unknown ({address})" : displayName,
                        IsPaired = false,
                        Rssi = rssi,
                        AdvertisementCount = 1
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

    /// <summary>
    /// Run a Bluetooth Classic (BR/EDR) inquiry via the Windows DeviceInformation watcher.
    /// Phones that are showing the Settings &gt; Bluetooth screen become discoverable on
    /// classic Bluetooth and broadcast their actual device name (e.g. "Sam's iPhone").
    /// We then merge that name onto matching BLE entries by Bluetooth address — the BR/EDR
    /// public address is usually identical to or off-by-one from the BLE public address,
    /// so we match by exact address first, then fall back to a +/-1 last-byte match
    /// (a documented behavior on dual-mode chips, including iPhones).
    /// </summary>
    private Task ScanForClassicBluetoothNamesAsync(
        Dictionary<string, DiscoveredDevice> devices,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Selector that returns Bluetooth (classic) devices, including non-paired ones
        // currently visible to inquiry. Requires the Bluetooth capability in the manifest
        // (already implied by the BLE selectors we use elsewhere).
        const string aqs =
            "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";
        string[] requestedProperties =
        {
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPaired",
            "System.ItemNameDisplay"
        };

        DeviceWatcher? watcher = null;
        try
        {
            watcher = DeviceInformation.CreateWatcher(
                aqs,
                requestedProperties,
                DeviceInformationKind.AssociationEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not create classic Bluetooth watcher");
            tcs.TrySetResult(true);
            return tcs.Task;
        }

        void Apply(DeviceInformation info)
        {
            try
            {
                if (info == null)
                {
                    return;
                }

                var name = info.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return;
                }

                string? addr = null;
                if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var raw) && raw is string s)
                {
                    // Format e.g. "aa:bb:cc:dd:ee:ff"
                    addr = s.Replace(":", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
                }
                if (string.IsNullOrWhiteSpace(addr))
                {
                    return;
                }

                lock (devices)
                {
                    // Exact address match
                    if (devices.TryGetValue(addr, out var existing))
                    {
                        if (IsPlaceholderName(existing.DeviceName))
                        {
                            existing.DeviceName = name;
                        }
                        return;
                    }

                    // Off-by-one last byte fallback: dual-mode chips (incl. iPhones) often
                    // expose BR/EDR and BLE public addresses that differ by 1 in the LSB.
                    if (ulong.TryParse(addr, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out var classicAddr))
                    {
                        foreach (var kv in devices)
                        {
                            if (!ulong.TryParse(kv.Key, System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out var bleAddr))
                            {
                                continue;
                            }

                            var diff = classicAddr > bleAddr ? classicAddr - bleAddr : bleAddr - classicAddr;
                            if (diff <= 1 && IsPlaceholderName(kv.Value.DeviceName))
                            {
                                kv.Value.DeviceName = name;
                                return;
                            }
                        }
                    }

                    // Otherwise, surface the classic-only device on its own entry so the
                    // user can still see/identify it.
                    devices[addr] = new DiscoveredDevice
                    {
                        DeviceId = addr,
                        DeviceName = name,
                        IsPaired = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Classic device parse failed");
            }
        }

        TypedEventHandler<DeviceWatcher, DeviceInformation> onAdded = (_, info) => Apply(info);
        TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> onUpdated = (_, _) => { };
        TypedEventHandler<DeviceWatcher, object> onCompleted = (_, _) => tcs.TrySetResult(true);
        TypedEventHandler<DeviceWatcher, object> onStopped = (_, _) => tcs.TrySetResult(true);

        watcher.Added += onAdded;
        watcher.Updated += onUpdated;
        watcher.EnumerationCompleted += onCompleted;
        watcher.Stopped += onStopped;

        try
        {
            watcher.Start();
            _logger.LogInformation("Classic Bluetooth inquiry started ({Seconds}s)", duration.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not start classic Bluetooth watcher");
            tcs.TrySetResult(true);
            return tcs.Task;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(duration);
        cts.Token.Register(() =>
        {
            try
            {
                if (watcher.Status == DeviceWatcherStatus.Started ||
                    watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    watcher.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error stopping classic watcher");
            }
            finally
            {
                cts.Dispose();
                tcs.TrySetResult(true);
            }
        });

        return tcs.Task;
    }

    /// <summary>
    /// For devices whose name is still a placeholder (e.g. "Apple device" / "Unknown (...)"),
    /// try to resolve a real device name. Windows often has it cached from a prior nearby
    /// connection; if not, opening a BluetoothLEDevice triggers a brief GATT connect that
    /// pulls the name from the Generic Access Profile service.
    /// </summary>
    private async Task ResolveDeviceNamesAsync(
        Dictionary<string, DiscoveredDevice> devices,
        CancellationToken cancellationToken)
    {
        DiscoveredDevice[] needsResolution;
        lock (devices)
        {
            needsResolution = devices.Values
                .Where(d => IsPlaceholderName(d.DeviceName))
                .ToArray();
        }

        if (needsResolution.Length == 0)
        {
            return;
        }

        // Per-device timeout — name lookups can hang on uncooperative phones.
        var tasks = needsResolution.Select(async d =>
        {
            using var perDeviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            perDeviceCts.CancelAfter(TimeSpan.FromSeconds(4));
            try
            {
                var resolved = await TryResolveNameAsync(d.DeviceId, perDeviceCts.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    lock (devices)
                    {
                        if (devices.TryGetValue(d.DeviceId, out var current) && IsPlaceholderName(current.DeviceName))
                        {
                            current.DeviceName = resolved!;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // skip — keep placeholder
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Name resolve failed for {Addr}", d.DeviceId);
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static bool IsPlaceholderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        if (name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Manufacturer-data labels we generated are also placeholders for the purpose of this pass.
        if (name.EndsWith(" device", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Equals("Google (Fast Pair)", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private async Task<string?> TryResolveNameAsync(string bluetoothAddress, CancellationToken cancellationToken)
    {
        if (!ulong.TryParse(bluetoothAddress, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var addr))
        {
            return null;
        }

        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask(cancellationToken).ConfigureAwait(false);
            if (bleDevice == null)
            {
                return null;
            }

            // Cached name (often populated for previously-seen devices).
            var cached = bleDevice.Name;
            if (!string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            // Try the Generic Access service's Device Name characteristic.
            // Uuid 1800 = Generic Access, 2A00 = Device Name.
            var gattResult = await bleDevice.GetGattServicesForUuidAsync(
                Guid.Parse("00001800-0000-1000-8000-00805F9B34FB"),
                BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false);

            if (gattResult.Status != GattCommunicationStatus.Success || gattResult.Services.Count == 0)
            {
                return null;
            }

            using var gap = gattResult.Services[0];
            var charsResult = await gap.GetCharacteristicsForUuidAsync(
                Guid.Parse("00002A00-0000-1000-8000-00805F9B34FB"),
                BluetoothCacheMode.Uncached).AsTask(cancellationToken).ConfigureAwait(false);

            if (charsResult.Status != GattCommunicationStatus.Success || charsResult.Characteristics.Count == 0)
            {
                return null;
            }

            var readResult = await charsResult.Characteristics[0]
                .ReadValueAsync(BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken).ConfigureAwait(false);

            if (readResult.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var reader = Windows.Storage.Streams.DataReader.FromBuffer(readResult.Value);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            var name = System.Text.Encoding.UTF8.GetString(bytes).Trim('\0', ' ');
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        finally
        {
            bleDevice?.Dispose();
        }
    }
#endif

#if WINDOWS
    /// <summary>
    /// Extract a friendly name from a BLE advertisement. Prefers the LocalName property,
    /// then falls back to scanning raw DataSections for Complete Local Name (0x09) or
    /// Shortened Local Name (0x08). Some adapters/iOS Scan Responses only surface the name
    /// via the data sections.
    /// </summary>
    private static string ExtractAdvertisementName(BluetoothLEAdvertisement advertisement)
    {
        if (advertisement == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(advertisement.LocalName))
        {
            return advertisement.LocalName;
        }

        try
        {
            foreach (var section in advertisement.DataSections)
            {
                // 0x09 Complete Local Name, 0x08 Shortened Local Name
                if (section.DataType == 0x09 || section.DataType == 0x08)
                {
                    var reader = Windows.Storage.Streams.DataReader.FromBuffer(section.Data);
                    var bytes = new byte[reader.UnconsumedBufferLength];
                    reader.ReadBytes(bytes);
                    var name = System.Text.Encoding.UTF8.GetString(bytes).Trim('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name;
                    }
                }
            }
        }
        catch
        {
            // ignore decode errors
        }
        return string.Empty;
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var deviceId = args.BluetoothAddress.ToString("X12");
        var rssi = args.RawSignalStrengthInDBm;
        var localName = ExtractAdvertisementName(args.Advertisement);

        // Calibration bypass: when the wizard is collecting, forward EVERY advert
        // as a raw reading regardless of tracked/enabled status. The wizard
        // buckets samples per address and locks onto the closest one. This makes
        // the calibration robust against iOS BLE privacy (rotating RPAs) where
        // the inbound advertisement address never matches the stored identity
        // address from pairing.
        if (IsCalibrating)
        {
            CalibrationReadingReceived?.Invoke(this, new BleDeviceReading
            {
                DeviceId = deviceId,
                DeviceName = !string.IsNullOrWhiteSpace(localName) ? localName : "Calibration target",
                Rssi = rssi,
                Timestamp = DateTime.UtcNow
            });
        }

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
            return;
        }

        // No exact address match. iOS phones (and some Android privacy modes)
        // broadcast Random Resolvable Private Addresses that rotate every
        // ~15 minutes, so the address from this advert never matches the one
        // stored at pairing time. For tracked devices flagged with
        // AssumePrivacyMode, route the *strongest currently-nearby* phone-like
        // advert as a reading for that tracked device. Filtering to phone-like
        // advertisements (Apple iPhone Nearby Info, etc.) prevents nearby
        // peripherals (mice, keyboards, AirPods) from being mistaken for the
        // tracked phone — which would otherwise keep the engine in Present
        // state forever and prevent lock-on-leave.
        if (IsLikelyPhoneAdvertisement(args.Advertisement))
        {
            TrackPrivacyModeAdvert(deviceId, rssi, localName);
        }

        // Surface unknown adverts in the discovery feed (existing behavior).
        var inferredName = !string.IsNullOrWhiteSpace(localName)
            ? localName
            : InferNameFromManufacturerData(args.Advertisement);

        if (!string.IsNullOrWhiteSpace(inferredName))
        {
            DeviceDiscovered?.Invoke(this, new DiscoveredDevice
            {
                DeviceId = deviceId,
                DeviceName = inferredName,
                IsPaired = false
            });
        }
    }

    // Per-advertisement-address sliding window of recent RSSI readings, used to
    // identify the closest currently-active source for AssumePrivacyMode tracked
    // devices. Source entries older than _privacyModeWindow are pruned.
    private readonly Dictionary<string, (short Rssi, DateTime LastSeen)> _recentAdvertSources = new();
    private readonly TimeSpan _privacyModeWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Tracks the latest RSSI for the given advertisement source and, if it is
    /// currently the strongest source within <see cref="_privacyModeWindow"/>,
    /// emits it as a reading for any enabled tracked device whose
    /// <see cref="TrackedDeviceConfig.AssumePrivacyMode"/> flag is true.
    /// </summary>
    private void TrackPrivacyModeAdvert(string sourceAddress, short rssi, string localName)
    {
        var privacyTargets = _settings.TrackedDevices
            .Where(d => d.Enabled && d.AssumePrivacyMode)
            .ToList();
        if (privacyTargets.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        lock (_recentAdvertSources)
        {
            _recentAdvertSources[sourceAddress] = (rssi, now);

            // Prune stale entries.
            var stale = _recentAdvertSources
                .Where(kv => now - kv.Value.LastSeen > _privacyModeWindow)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in stale)
            {
                _recentAdvertSources.Remove(key);
            }

            // Is the current source the strongest within the window?
            var strongest = _recentAdvertSources
                .OrderByDescending(kv => kv.Value.Rssi)
                .First();

            if (strongest.Key != sourceAddress)
            {
                return;
            }
        }

        // Forward this RSSI as a reading for each privacy-mode tracked device.
        // Most users have one phone, so this fires once. The reading's DeviceId
        // is the tracked device's stored id so ProximityEngine accumulates it
        // under a stable key and the configured thresholds are applied.
        foreach (var t in privacyTargets)
        {
            DeviceDetected?.Invoke(this, new BleDeviceReading
            {
                DeviceId = t.DeviceId,
                DeviceName = !string.IsNullOrWhiteSpace(localName) ? localName : t.DeviceName,
                Rssi = rssi,
                Timestamp = now
            });
        }
    }

    /// <summary>
    /// Heuristic: returns true if the advertisement looks like it came from a
    /// modern smartphone (rather than a peripheral such as a mouse, keyboard,
    /// or AirPods). Used by privacy-mode routing to avoid mistaking a closer
    /// peripheral for the tracked phone — a misclassification that would
    /// keep the engine in Present state and prevent lock-on-leave.
    /// <para>
    /// iPhone detection: Apple manufacturer (0x004C) with Nearby Info
    /// (subtype 0x10) or Nearby Action (0x0F). These are broadcast continuously
    /// by iOS while the device is unlocked. AirPods/AirTags use different
    /// subtypes (0x07 Proximity Pairing, 0x12 Find My) and are excluded.
    /// </para>
    /// <para>
    /// Other phone OSes (Samsung, Google, Xiaomi, etc.) are accepted by
    /// company identifier match — those vendors don't typically ship BLE
    /// peripherals broadcasting under their own company ID.
    /// </para>
    /// </summary>
    private static bool IsLikelyPhoneAdvertisement(BluetoothLEAdvertisement? advertisement)
    {
        if (advertisement?.ManufacturerData == null || advertisement.ManufacturerData.Count == 0)
        {
            return false;
        }

        foreach (var md in advertisement.ManufacturerData)
        {
            switch (md.CompanyId)
            {
                case 0x004C: // Apple
                    {
                        // Read first payload byte to distinguish iPhone from AirPods/AirTags.
                        if (md.Data == null || md.Data.Length == 0)
                        {
                            continue;
                        }
                        try
                        {
                            var reader = Windows.Storage.Streams.DataReader.FromBuffer(md.Data);
                            if (reader.UnconsumedBufferLength == 0)
                            {
                                continue;
                            }

                            var subtype = reader.ReadByte();
                            // 0x10 Nearby Info, 0x0F Nearby Action — iPhone/iPad/Mac.
                            if (subtype == 0x10 || subtype == 0x0F)
                            {
                                return true;
                            }
                        }
                        catch
                        {
                            // ignore decode errors for this section
                        }
                        break;
                    }
                case 0x0075: // Samsung
                case 0x00E0: // Google
                case 0x038F: // Xiaomi
                case 0x0131: // Huawei
                case 0x010F: // OnePlus
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a friendly device label inferred from the advertisement's manufacturer data
    /// (Company Identifier). Useful for devices that don't broadcast a LocalName, such as iPhones.
    /// </summary>
    private static string InferNameFromManufacturerData(BluetoothLEAdvertisement advertisement)
    {
        if (advertisement?.ManufacturerData == null || advertisement.ManufacturerData.Count == 0)
        {
            return string.Empty;
        }

        foreach (var md in advertisement.ManufacturerData)
        {
            // Bluetooth SIG assigned Company Identifiers
            // https://www.bluetooth.com/specifications/assigned-numbers/
            switch (md.CompanyId)
            {
                case 0x004C: return "Apple device";
                case 0x0006: return "Microsoft device";
                case 0x00E0: return "Google device";
                case 0x0075: return "Samsung device";
                case 0x00D7: return "Google (Fast Pair)";
                case 0x0087: return "Garmin device";
                case 0x0157: return "Anhui Huami (Mi/Amazfit)";
                case 0x038F: return "Xiaomi device";
                case 0x0131: return "Huawei device";
                case 0x010F: return "OnePlus device";
            }
        }

        return string.Empty;
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

    /// <summary>
    /// Attempt to pair a BLE device by its Bluetooth address ("X12" hex string).
    /// Uses Windows custom pairing with ConfirmOnly + DisplayPin ceremonies, which covers
    /// iPhones and most modern phones. The user must approve the prompt on the phone.
    /// </summary>
    public async Task<BlePairingResult> PairDeviceAsync(string bluetoothAddress, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        _logger.LogInformation("PairDeviceAsync called with address={Addr}", bluetoothAddress);
        if (string.IsNullOrWhiteSpace(bluetoothAddress))
        {
            return new BlePairingResult(false, "No Bluetooth address provided");
        }

        if (!ulong.TryParse(bluetoothAddress, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var addr))
        {
            return new BlePairingResult(false, $"Invalid Bluetooth address: {bluetoothAddress}");
        }

        BluetoothLEDevice? bleDevice = null;
        try
        {
            bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(addr).AsTask(cancellationToken);
            if (bleDevice == null)
            {
                // Device not advertising as BLE — fall back to classic Bluetooth pairing.
                // This is the path iPhones (and many phones in Settings > Bluetooth) take.
                _logger.LogInformation("BLE device not found at {Addr}, falling back to classic Bluetooth pairing", bluetoothAddress);
                return await PairClassicDeviceAsync(addr, bluetoothAddress, cancellationToken).ConfigureAwait(false);
            }

            var pairing = bleDevice.DeviceInformation.Pairing;
            if (pairing.IsPaired)
            {
                return new BlePairingResult(true, "Already paired");
            }

            var custom = pairing.Custom;
            void OnPairingRequested(DeviceInformationCustomPairing s, DevicePairingRequestedEventArgs args)
            {
                _logger.LogInformation("BLE pairing requested: ceremony={Ceremony}, pin={Pin}",
                    args.PairingKind,
                    args.PairingKind == DevicePairingKinds.DisplayPin || args.PairingKind == DevicePairingKinds.ConfirmPinMatch
                        ? args.Pin
                        : "<n/a>");

                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                    case DevicePairingKinds.DisplayPin:
                    case DevicePairingKinds.ConfirmPinMatch:
                        // Just Works (iPhone, many Androids) and Numeric Comparison (Android SSP).
                        // Auto-accept; the user confirms on their phone.
                        args.Accept();
                        break;
                    case DevicePairingKinds.ProvidePin:
                        // Some Android phones / Classic-BT pairings: PC must type a PIN shown on the phone.
                        if (PinRequested != null)
                        {
                            var deferral = args.GetDeferral();
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var pin = await PinRequested(bluetoothAddress).ConfigureAwait(false);
                                    if (!string.IsNullOrWhiteSpace(pin))
                                    {
                                        args.Accept(pin);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "PinRequested handler failed");
                                }
                                finally
                                {
                                    deferral.Complete();
                                }
                            });
                        }
                        else
                        {
                            _logger.LogWarning("Pairing requires a PIN but no PinRequested handler is registered");
                        }
                        break;
                }
            }

            custom.PairingRequested += OnPairingRequested;
            try
            {
                var ceremonies = DevicePairingKinds.ConfirmOnly
                    | DevicePairingKinds.DisplayPin
                    | DevicePairingKinds.ConfirmPinMatch
                    | DevicePairingKinds.ProvidePin;

                var result = await custom.PairAsync(ceremonies, DevicePairingProtectionLevel.EncryptionAndAuthentication)
                    .AsTask(cancellationToken);

                _logger.LogInformation("BLE pairing result for {Addr}: {Status}", bluetoothAddress, result.Status);

                return result.Status switch
                {
                    DevicePairingResultStatus.Paired => new BlePairingResult(true, "Paired"),
                    DevicePairingResultStatus.AlreadyPaired => new BlePairingResult(true, "Already paired"),
                    _ => new BlePairingResult(false, result.Status.ToString())
                };
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BLE pairing failed for {Addr}", bluetoothAddress);
            return new BlePairingResult(false, ex.Message);
        }
        finally
        {
            bleDevice?.Dispose();
        }
#else
        await Task.Delay(100, cancellationToken);
        return new BlePairingResult(false, "Pairing is only supported on Windows");
#endif
    }

#if WINDOWS
    /// <summary>
    /// Pair over Bluetooth Classic (BR/EDR). Used for phones that surfaced via the
    /// classic inquiry rather than BLE advertisements (typical for iPhones).
    /// </summary>
    private async Task<BlePairingResult> PairClassicDeviceAsync(ulong addr, string bluetoothAddress, CancellationToken cancellationToken)
    {
        Windows.Devices.Bluetooth.BluetoothDevice? classic = null;
        try
        {
            classic = await Windows.Devices.Bluetooth.BluetoothDevice
                .FromBluetoothAddressAsync(addr)
                .AsTask(cancellationToken);

            if (classic == null)
            {
                return new BlePairingResult(false, "Device not found on classic Bluetooth either. Keep the phone's Bluetooth screen open and try again.");
            }

            var pairing = classic.DeviceInformation.Pairing;
            if (pairing.IsPaired)
            {
                return new BlePairingResult(true, "Already paired");
            }

            var custom = pairing.Custom;
            void OnPairingRequested(DeviceInformationCustomPairing s, DevicePairingRequestedEventArgs args)
            {
                _logger.LogInformation("Classic pairing requested: ceremony={Ceremony}, pin={Pin}",
                    args.PairingKind,
                    args.PairingKind == DevicePairingKinds.DisplayPin || args.PairingKind == DevicePairingKinds.ConfirmPinMatch
                        ? args.Pin
                        : "<n/a>");

                switch (args.PairingKind)
                {
                    case DevicePairingKinds.ConfirmOnly:
                    case DevicePairingKinds.DisplayPin:
                    case DevicePairingKinds.ConfirmPinMatch:
                        args.Accept();
                        break;
                    case DevicePairingKinds.ProvidePin:
                        if (PinRequested != null)
                        {
                            var deferral = args.GetDeferral();
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var pin = await PinRequested(bluetoothAddress).ConfigureAwait(false);
                                    if (!string.IsNullOrWhiteSpace(pin))
                                    {
                                        args.Accept(pin);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "PinRequested handler failed");
                                }
                                finally
                                {
                                    deferral.Complete();
                                }
                            });
                        }
                        else
                        {
                            _logger.LogWarning("Classic pairing requires a PIN but no PinRequested handler is registered");
                        }
                        break;
                }
            }

            custom.PairingRequested += OnPairingRequested;
            try
            {
                var ceremonies = DevicePairingKinds.ConfirmOnly
                    | DevicePairingKinds.DisplayPin
                    | DevicePairingKinds.ConfirmPinMatch
                    | DevicePairingKinds.ProvidePin;

                var result = await custom.PairAsync(ceremonies, DevicePairingProtectionLevel.Default)
                    .AsTask(cancellationToken);

                _logger.LogInformation("Classic pairing result for {Addr}: {Status}", bluetoothAddress, result.Status);

                return result.Status switch
                {
                    DevicePairingResultStatus.Paired => new BlePairingResult(true, "Paired (classic)"),
                    DevicePairingResultStatus.AlreadyPaired => new BlePairingResult(true, "Already paired"),
                    _ => new BlePairingResult(false, result.Status.ToString())
                };
            }
            finally
            {
                custom.PairingRequested -= OnPairingRequested;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classic pairing failed for {Addr}", bluetoothAddress);
            return new BlePairingResult(false, ex.Message);
        }
        finally
        {
            classic?.Dispose();
        }
    }
#endif
}

/// <summary>
/// Result of a BLE pairing attempt.
/// </summary>
public record BlePairingResult(bool Success, string Message);

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
    /// <summary>Strongest RSSI observed during the discovery window (dBm). 0 = not seen on air.</summary>
    public short Rssi { get; set; }
    /// <summary>Number of advertisements received during discovery (helps spot active devices).</summary>
    public int AdvertisementCount { get; set; }
}
