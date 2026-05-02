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
                IsEnabled = device.Enabled
            });
        }
    }

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        StatusText = "Discovering devices...";
        var devices = await _bleScanner.DiscoverDevicesAsync();

        _dispatcher.Invoke(() =>
        {
            foreach (var device in devices)
            {
                if (!TrackedDevices.Any(d => d.DeviceId == device.DeviceId))
                {
                    TrackedDevices.Add(new DeviceViewModel
                    {
                        DeviceId = device.DeviceId,
                        DeviceName = device.DeviceName,
                        IsEnabled = false
                    });
                }
            }
            StatusText = $"Found {devices.Count} devices";
        });
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
                DistanceMeters = _distanceEstimator.EstimateDistance(evt.SmoothedRssi);
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
        if (EventLog.Count > MaxEventLogEntries) EventLog.RemoveAt(EventLog.Count - 1);
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
    private string _lastState = "Unknown";

    [ObservableProperty]
    private double _lastRssi;
}

