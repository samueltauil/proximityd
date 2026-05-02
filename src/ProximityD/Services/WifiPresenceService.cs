using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;

namespace ProximityD.Services;

/// <summary>
/// Detects device presence by pinging a hostname or IP address over the network.
/// Used as a secondary presence signal alongside BLE RSSI.
/// </summary>
public class WifiPresenceService : IDisposable
{
    private readonly ILogger<WifiPresenceService> _logger;
    private readonly AppSettings _settings;
    private Timer? _timer;
    private bool _disposed;
    private readonly object _lock = new();

    /// <summary>Raised when the detected presence state changes.</summary>
    public event EventHandler<WifiPresenceState>? PresenceChanged;

    /// <summary>Gets the current WiFi presence state.</summary>
    public WifiPresenceState CurrentState { get; private set; } = WifiPresenceState.Unknown;

    public WifiPresenceService(ILogger<WifiPresenceService> logger, AppSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>Starts polling the configured hostname for presence.</summary>
    public void Start()
    {
        if (!_settings.EnableWifiPresence || string.IsNullOrWhiteSpace(_settings.WifiDeviceHostname))
        {
            _logger.LogDebug("WiFi presence not enabled or hostname not configured.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.WifiPingIntervalSeconds));
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = new Timer(PollAsync, null, TimeSpan.Zero, interval);
        }

        _logger.LogInformation("WiFi presence polling started for {Host} every {Interval}s",
            _settings.WifiDeviceHostname, _settings.WifiPingIntervalSeconds);
    }

    /// <summary>Stops polling.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
        _logger.LogDebug("WiFi presence polling stopped.");
    }

    private async void PollAsync(object? state)
    {
        try
        {
            var newState = await PingAsync(_settings.WifiDeviceHostname);
            UpdateState(newState);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during WiFi presence poll");
            UpdateState(WifiPresenceState.Unknown);
        }
    }

    internal virtual async Task<WifiPresenceState> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            return reply.Status == IPStatus.Success
                ? WifiPresenceState.Present
                : WifiPresenceState.Away;
        }
        catch (PingException)
        {
            return WifiPresenceState.Away;
        }
        catch
        {
            return WifiPresenceState.Unknown;
        }
    }

    private void UpdateState(WifiPresenceState newState)
    {
        if (newState == CurrentState)
        {
            return;
        }
        CurrentState = newState;
        _logger.LogInformation("WiFi presence state changed to {State}", newState);
        PresenceChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
    }
}

/// <summary>Represents the WiFi-based presence state of a device.</summary>
public enum WifiPresenceState
{
    /// <summary>Device responded to ping — it is present on the network.</summary>
    Present,
    /// <summary>Device did not respond — it is likely away.</summary>
    Away,
    /// <summary>Presence could not be determined (network error, not configured).</summary>
    Unknown
}
