using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Models;

namespace ProximityD.Services;

/// <summary>
/// Background service that orchestrates BLE scanning, proximity detection, and actions.
/// </summary>
public class ProximityBackgroundService : BackgroundService
{
    private readonly ILogger<ProximityBackgroundService> _logger;
    private readonly BleScanner _bleScanner;
    private readonly ProximityEngine _proximityEngine;
    private readonly WindowsActionService _actionService;
    private readonly WifiPresenceService _wifiPresenceService;
    private readonly AppSettings _settings;

    public event EventHandler<ProximityEvent>? ProximityStateChanged;
    public event EventHandler<string>? StatusChanged;

    public ProximityBackgroundService(
        ILogger<ProximityBackgroundService> logger,
        BleScanner bleScanner,
        ProximityEngine proximityEngine,
        WindowsActionService actionService,
        WifiPresenceService wifiPresenceService,
        AppSettings settings)
    {
        _logger = logger;
        _bleScanner = bleScanner;
        _proximityEngine = proximityEngine;
        _actionService = actionService;
        _wifiPresenceService = wifiPresenceService;
        _settings = settings;

        // Wire up events
        _bleScanner.DeviceDetected += OnDeviceDetected;
        _proximityEngine.ProximityChanged += OnProximityChanged;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProximityD background service starting...");
        StatusChanged?.Invoke(this, "Starting...");

        if (_settings.TrackedDevices.Count == 0)
        {
            _logger.LogWarning("No tracked devices configured. Please add a device in settings.");
            StatusChanged?.Invoke(this, "No devices configured");
        }

        _bleScanner.StartScanning();
        _wifiPresenceService.PresenceChanged += OnWifiPresenceChanged;
        _wifiPresenceService.Start();
        StatusChanged?.Invoke(this, "Scanning...");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Periodically check for lost devices
                _proximityEngine.CheckForLostDevices();
                await Task.Delay(_settings.ScanIntervalMs, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            _wifiPresenceService.PresenceChanged -= OnWifiPresenceChanged;
            _wifiPresenceService.Stop();
            _bleScanner.StopScanning();
            StatusChanged?.Invoke(this, "Stopped");
            _logger.LogInformation("ProximityD background service stopped");
        }
    }

    private void OnDeviceDetected(object? sender, BleDeviceReading reading)
    {
        _proximityEngine.ProcessReading(reading.DeviceId, reading.DeviceName, reading.Rssi);
    }

    private void OnProximityChanged(object? sender, ProximityEvent evt)
    {
        ProximityStateChanged?.Invoke(this, evt);
        _actionService.OnProximityChanged(evt.State);
    }

    private void OnWifiPresenceChanged(object? sender, WifiPresenceState state)
    {
        _logger.LogInformation("WiFi presence state: {State}", state);
        StatusChanged?.Invoke(this, $"WiFi: {state}");
    }

    public override void Dispose()
    {
        _bleScanner.DeviceDetected -= OnDeviceDetected;
        _proximityEngine.ProximityChanged -= OnProximityChanged;
        _bleScanner.Dispose();
        _proximityEngine.Dispose();
        _wifiPresenceService.Dispose();
        base.Dispose();
    }
}
