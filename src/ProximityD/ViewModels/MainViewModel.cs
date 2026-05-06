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
        _backgroundService.StatusChanged += OnStatusChanged;
        _bleScanner.DeviceDetected += OnDeviceDetected;

        // Load tracked devices
        foreach (var device in settings.TrackedDevices)
        {
            TrackedDevices.Add(new DeviceViewModel
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                MacAddress = device.MacAddress,
                IsEnabled = device.Enabled,
                IsPaired = true
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
                        existing.IsPaired = device.IsPaired || existing.IsPaired;
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

    [RelayCommand]
    private async Task PairDeviceAsync(DeviceViewModel? device)
    {
        if (device == null)
        {
            return;
        }

        StatusText = $"Pairing {device.DeviceName}...";
        AddLogEntry($"Pairing {device.DeviceName} ({device.DeviceId})... approve the prompt on the device.");
        try
        {
            var result = await _bleScanner.PairDeviceAsync(device.DeviceId);
            _dispatcher.Invoke(() =>
            {
                if (result.Success)
                {
                    device.IsPaired = true;
                    device.IsEnabled = true;
                    StatusText = $"Paired {device.DeviceName}";
                    AddLogEntry($"Paired {device.DeviceName}: {result.Message}");
                    SaveSettings();
                }
                else
                {
                    StatusText = $"Pairing failed: {result.Message}";
                    AddLogEntry($"Pairing failed for {device.DeviceName}: {result.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pairing failed");
            _dispatcher.Invoke(() =>
            {
                StatusText = $"Pairing failed: {ex.Message}";
                AddLogEntry($"Pairing exception: {ex.Message}");
            });
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
            CurrentRssi = evt.Rssi;
            SmoothedRssi = evt.SmoothedRssi;
            if (_settings.EnableDistanceMode)
            {
                DistanceMeters = _distanceEstimator.EstimateDistance(evt.SmoothedRssi);
            }
            SignalGraph.AddDataPoint(evt.Timestamp, evt.Rssi, evt.SmoothedRssi);
            // Only feed readings for the selected calibration device to avoid mixing signals.
            // Require an explicit device selection; ignore readings when none is chosen.
            if (!string.IsNullOrEmpty(CalibrationWizard.SelectedDeviceId) &&
                CalibrationWizard.SelectedDeviceId == evt.DeviceId)
            {
                CalibrationWizard.OnRssiReading(evt.SmoothedRssi);
            }
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

    private void OnStatusChanged(object? sender, string status)
    {
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

