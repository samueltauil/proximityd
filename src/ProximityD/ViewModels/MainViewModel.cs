using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Filters;
using ProximityD.Models;
using ProximityD.Services;

namespace ProximityD.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly AppSettings _settings;
    private readonly BleScanner _bleScanner;
    private readonly ProximityEngine _proximityEngine;
    private readonly ProximityBackgroundService _backgroundService;
    private readonly Dispatcher _dispatcher;
    private readonly PathLossDistanceEstimator _distanceEstimator;

    [ObservableProperty]
    private string _statusText = "Initializing...";

    [ObservableProperty]
    private string _proximityStateText = "Unknown";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private double _currentRssi;

    [ObservableProperty]
    private double _smoothedRssi;

    [ObservableProperty]
    private double _distanceMeters;

    [ObservableProperty]
    private bool _autoLockEnabled;

    [ObservableProperty]
    private bool _autoUnlockEnabled;

    [ObservableProperty]
    private int _lockThreshold;

    [ObservableProperty]
    private int _unlockThreshold;

    [ObservableProperty]
    private int _lockDelay;

    [ObservableProperty]
    private int _unlockDelay;

    private const int MaxEventLogEntries = 100;

    public ObservableCollection<DeviceViewModel> TrackedDevices { get; } = new();
    public ObservableCollection<string> EventLog { get; } = new();

    public SignalGraphViewModel SignalGraph { get; }
    public CalibrationWizardViewModel CalibrationWizard { get; }

    public MainViewModel(
        ILogger<MainViewModel> logger,
        AppSettings settings,
        BleScanner bleScanner,
        ProximityEngine proximityEngine,
        ProximityBackgroundService backgroundService,
        PathLossDistanceEstimator distanceEstimator,
        SignalGraphViewModel signalGraphViewModel,
        CalibrationWizardViewModel calibrationWizardViewModel)
    {
        _logger = logger;
        _settings = settings;
        _bleScanner = bleScanner;
        _proximityEngine = proximityEngine;
        _backgroundService = backgroundService;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _distanceEstimator = distanceEstimator;
        SignalGraph = signalGraphViewModel;
        CalibrationWizard = calibrationWizardViewModel;

        // Initialize from settings
        AutoLockEnabled = settings.EnableAutoLock;
        AutoUnlockEnabled = settings.EnableAutoUnlock;
        LockThreshold = settings.LockRssiThreshold;
        UnlockThreshold = settings.UnlockRssiThreshold;
        LockDelay = settings.LockDelaySeconds;
        UnlockDelay = settings.UnlockDelaySeconds;

        // Subscribe to events
        _backgroundService.ProximityStateChanged += OnProximityStateChanged;
        _backgroundService.ReadingProcessed += OnReadingProcessed;
        _backgroundService.StatusChanged += OnStatusChanged;
        _bleScanner.DeviceDetected += OnDeviceDetected;
        _bleScanner.CalibrationReadingReceived += OnCalibrationReading;

        // Bridge wizard collection state to the BleScanner: when the wizard starts
        // collecting, route raw advertisements for the selected device into the
        // wizard (and signal graph) regardless of whether it is enabled / tracked.
        CalibrationWizard.PropertyChanged += OnCalibrationWizardPropertyChanged;

        // Load tracked devices. We don't persist IsPaired in settings, so initialize to
        // false; the next discovery pass will set it authoritatively. (Previously we
        // optimistically set IsPaired=true here, which combined with a never-downgrade
        // merge below caused devices to remain "paired" in the UI even when they were
        // not actually paired.)
        foreach (var device in settings.TrackedDevices)
        {
            TrackedDevices.Add(new DeviceViewModel
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                MacAddress = device.MacAddress,
                IsEnabled = device.Enabled,
                IsPaired = false
            });
        }
    }

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        StatusText = "Discovering... (15s)";
        AddLogEntry("Discovery started. Keep iOS/Android Settings > Bluetooth open during the scan so phones broadcast their name.");
        try
        {
            var devices = await _bleScanner.DiscoverDevicesAsync();

            _dispatcher.Invoke(() =>
            {
                int added = 0;
                // Sort by signal strength: closest device floats to top so users can identify
                // their phone by walking up to the PC during discovery.
                var ordered = devices
                    .OrderByDescending(d => d.IsPaired)
                    .ThenByDescending(d => d.Rssi == 0 ? short.MinValue : d.Rssi)
                    .ToList();

                foreach (var device in ordered)
                {
                    var existing = TrackedDevices.FirstOrDefault(d => d.DeviceId == device.DeviceId);
                    if (existing == null)
                    {
                        TrackedDevices.Add(new DeviceViewModel
                        {
                            DeviceId = device.DeviceId,
                            DeviceName = device.DeviceName,
                            IsEnabled = false,
                            IsPaired = device.IsPaired,
                            LastRssi = device.Rssi
                        });
                        added++;
                    }
                    else
                    {
                        // Trust the discovery result for paired status — it is the
                        // authoritative source (Phase 1: paired BLE + classic
                        // enumeration). Don't OR with stale UI state.
                        existing.IsPaired = device.IsPaired;
                        if (device.Rssi != 0)
                        {
                            existing.LastRssi = device.Rssi;
                        }
                        if (string.IsNullOrWhiteSpace(existing.DeviceName) || existing.DeviceName == "Unknown")
                        {
                            existing.DeviceName = device.DeviceName;
                        }
                    }
                }
                StatusText = $"Found {devices.Count} device(s) ({added} new)";
                AddLogEntry($"Discovery complete: {devices.Count} device(s), {added} new");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device discovery failed");
            _dispatcher.Invoke(() =>
            {
                StatusText = $"Discovery failed: {ex.Message}";
                AddLogEntry($"Discovery failed: {ex.Message}");
            });
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.EnableAutoLock = AutoLockEnabled;
        _settings.EnableAutoUnlock = AutoUnlockEnabled;
        _settings.LockRssiThreshold = LockThreshold;
        _settings.UnlockRssiThreshold = UnlockThreshold;
        _settings.LockDelaySeconds = LockDelay;
        _settings.UnlockDelaySeconds = UnlockDelay;

        _settings.TrackedDevices = TrackedDevices
            .Select(d => new TrackedDeviceConfig
            {
                DeviceId = d.DeviceId,
                DeviceName = d.DeviceName,
                MacAddress = d.MacAddress,
                Enabled = d.IsEnabled
            }).ToList();

        _settings.Save();
        AddLogEntry("Settings saved");
    }

    [RelayCommand]
    private void ToggleDevice(DeviceViewModel device)
    {
        device.IsEnabled = !device.IsEnabled;
        SaveSettings();
    }

    [RelayCommand]
    private void ForgetDevice(DeviceViewModel? device)
    {
        if (device == null) return;
        TrackedDevices.Remove(device);
        AddLogEntry($"Forgot {device.DeviceName} ({device.DeviceId})");
        SaveSettings();
    }

    private bool _suppressBackgroundStatus;

    [RelayCommand]
    private async Task PairDeviceAsync(DeviceViewModel? device)
    {
        if (device == null)
        {
            AddLogEntry("Pair: no device selected.");
            return;
        }

        if (_pairFlowHandler == null)
        {
            AddLogEntry("Pair: UI handler not registered.");
            return;
        }

        _suppressBackgroundStatus = true;
        StatusText = $"Pair {device.DeviceName}: follow the instructions...";
        AddLogEntry($"Pair flow opened for {device.DeviceName} ({device.DeviceId}).");

        try
        {
            var confirmed = await _pairFlowHandler(device.DeviceName, device.DeviceId);
            if (!confirmed)
            {
                StatusText = "Pairing cancelled.";
                AddLogEntry("Pairing cancelled by user.");
                return;
            }

            StatusText = "Refreshing device list...";
            AddLogEntry("Refreshing device list to detect newly paired devices...");
            _suppressBackgroundStatus = false;
            await DiscoverDevicesAsync();

            // Mark this device's row as paired if the refresh confirmed it.
            _dispatcher.Invoke(() =>
            {
                var refreshed = TrackedDevices.FirstOrDefault(d =>
                    string.Equals(d.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase));
                if (refreshed != null && refreshed.IsPaired)
                {
                    refreshed.IsEnabled = true;
                    SaveSettings();
                    StatusText = $"{refreshed.DeviceName} is paired.";
                    AddLogEntry($"{refreshed.DeviceName} is now paired.");
                }
                else
                {
                    StatusText = "Pair not detected yet. If you completed pairing on the phone, click Discover Devices again.";
                    AddLogEntry("Could not confirm pair status from refresh. Try Discover Devices again.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pairing flow failed");
            _dispatcher.Invoke(() =>
            {
                StatusText = $"Pair error: {ex.Message}";
                AddLogEntry($"Pair error: {ex.Message}");
            });
        }
        finally
        {
            _suppressBackgroundStatus = false;
        }
    }

    [RelayCommand]
    private void ShowCalibrationWizard()
    {
        // Handled by the view layer
        AddLogEntry("Opening calibration wizard...");
    }

    private void OnProximityStateChanged(object? sender, ProximityEvent evt)
    {
        _dispatcher.Invoke(() =>
        {
            ProximityStateText = evt.State.ToString();
            AddLogEntry($"[{evt.Timestamp:HH:mm:ss}] {evt.DeviceName}: {evt.State} (RSSI: {evt.Rssi}, Smoothed: {evt.SmoothedRssi:F1})");

            // Update device in list
            var device = TrackedDevices.FirstOrDefault(d => d.DeviceId == evt.DeviceId);
            if (device != null)
            {
                device.LastState = evt.State.ToString();
                device.LastRssi = evt.Rssi;
            }
        });
    }

    private void OnReadingProcessed(object? sender, ProximityEvent evt)
    {
        _dispatcher.Invoke(() =>
        {
            CurrentRssi = evt.Rssi;
            SmoothedRssi = evt.SmoothedRssi;
            if (_settings.EnableDistanceMode)
            {
                DistanceMeters = _distanceEstimator.EstimateDistance(evt.SmoothedRssi);
            }

            // Continuous live signal graph (every reading, not just state changes).
            SignalGraph.AddDataPoint(evt.Timestamp, evt.Rssi, evt.SmoothedRssi);

            // Continuous calibration sample feed for the device the user picked in the wizard.
            if (!string.IsNullOrEmpty(CalibrationWizard.SelectedDeviceId) &&
                CalibrationWizard.SelectedDeviceId == evt.DeviceId)
            {
                CalibrationWizard.OnRssiReading(evt.SmoothedRssi);
            }

            // Update RSSI on the matching list row even between state transitions.
            var device = TrackedDevices.FirstOrDefault(d => d.DeviceId == evt.DeviceId);
            if (device != null)
            {
                device.LastRssi = evt.Rssi;
            }
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        if (_suppressBackgroundStatus) return;
        _dispatcher.Invoke(() => StatusText = status);
    }

    private void OnDeviceDetected(object? sender, BleDeviceReading reading)
    {
        _dispatcher.Invoke(() =>
        {
            CurrentRssi = reading.Rssi;
            IsScanning = true;
        });
    }

    private void OnCalibrationReading(object? sender, BleDeviceReading reading)
    {
        // Raw advertisement received while the wizard is collecting. Pass the
        // address through so the wizard can bucket per-device and lock onto the
        // closest one (handles iOS rotating BLE addresses).
        _dispatcher.Invoke(() =>
        {
            CalibrationWizard.OnRssiReading(reading.DeviceId, reading.Rssi);
            SignalGraph.AddDataPoint(reading.Timestamp, reading.Rssi, reading.Rssi);
        });
    }

    private void OnCalibrationWizardPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalibrationWizardViewModel.IsCollectingSamples))
        {
            // Toggle scanner-wide calibration mode: forward every advert as a
            // raw reading regardless of pairing/tracking. Robust against iOS
            // BLE privacy where the inbound advertisement address differs from
            // the stored classic Bluetooth address.
            _bleScanner.IsCalibrating = CalibrationWizard.IsCollectingSamples;
        }
    }

    private void AddLogEntry(string message)
    {
        EventLog.Insert(0, message);
        if (EventLog.Count > MaxEventLogEntries)
        {
            EventLog.RemoveAt(EventLog.Count - 1);
        }
    }

    /// <summary>
    /// Register a UI handler that prompts the user for a Bluetooth PIN. Used when a remote
    /// device (typically Android in legacy/SSP-PIN mode) requires the PC to enter a PIN
    /// shown on the phone.
    /// </summary>
    public void SetPinPromptHandler(Func<string, Task<string?>> handler)
    {
        _bleScanner.PinRequested = handler;
    }

    /// <summary>
    /// Called by the View to register a handler that displays the pairing instructions
    /// dialog (and opens Windows Bluetooth Settings). Returns true if the user clicked
    /// "I've paired", false on cancel.
    /// </summary>
    private Func<string, string, Task<bool>>? _pairFlowHandler;
    public void SetPairFlowHandler(Func<string, string, Task<bool>> handler)
    {
        _pairFlowHandler = handler;
    }
}

public partial class DeviceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private string _macAddress = string.Empty;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isPaired;

    [ObservableProperty]
    private string _lastState = "Unknown";

    [ObservableProperty]
    private double _lastRssi;
}

