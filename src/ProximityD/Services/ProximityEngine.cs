using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Filters;
using ProximityD.Models;

namespace ProximityD.Services;

/// <summary>
/// Core proximity detection engine.
/// Processes raw RSSI signals through filters and determines proximity state using hysteresis.
/// Thread-safe: can be called from BLE callbacks and background service loop concurrently.
/// </summary>
public class ProximityEngine : IDisposable
{
    private readonly ILogger<ProximityEngine> _logger;
    private readonly AppSettings _settings;
    private readonly ConcurrentDictionary<string, DeviceState> _deviceStates = new();

    public event EventHandler<ProximityEvent>? ProximityChanged;

    /// <summary>
    /// Fired for every processed reading (not just state transitions). Used by UI for
    /// real-time signal-graph plotting and calibration sample collection.
    /// </summary>
    public event EventHandler<ProximityEvent>? ReadingProcessed;

    public ProximityEngine(ILogger<ProximityEngine> logger, AppSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    /// <summary>
    /// Process a new RSSI reading for a device.
    /// </summary>
    public ProximityState ProcessReading(string deviceId, string deviceName, short rssi)
    {
        var state = _deviceStates.GetOrAdd(deviceId, id => CreateDeviceState(id));

        lock (state)
        {
            // Update signal filter
            var smoothedRssi = state.Filter.Update(rssi);
            state.LastRssi = rssi;
            state.SmoothedRssi = smoothedRssi;
            state.LastSeen = DateTime.UtcNow;
            state.LastKnownName = deviceName;

            // Determine proximity state with hysteresis
            var newState = DetermineState(state, smoothedRssi);

            // Always emit a per-reading event so UI consumers (signal graph, calibration
            // wizard) get a continuous data stream — ProximityChanged below only fires
            // on transitions.
            ReadingProcessed?.Invoke(this, new ProximityEvent
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                State = newState,
                Rssi = rssi,
                SmoothedRssi = smoothedRssi,
                Timestamp = DateTime.UtcNow
            });

            if (newState != state.CurrentState)
            {
                var previousState = state.CurrentState;
                state.CurrentState = newState;

                _logger.LogInformation(
                    "Device {DeviceName} ({DeviceId}) state changed: {OldState} -> {NewState} (RSSI: {Rssi}, Smoothed: {Smoothed:F1})",
                    deviceName, deviceId, previousState, newState, rssi, smoothedRssi);

                ProximityChanged?.Invoke(this, new ProximityEvent
                {
                    DeviceId = deviceId,
                    DeviceName = deviceName,
                    State = newState,
                    Rssi = rssi,
                    SmoothedRssi = smoothedRssi,
                    Timestamp = DateTime.UtcNow
                });
            }

            return newState;
        }
    }

    /// <summary>
    /// Check for devices that have been lost (no signal for extended period).
    /// </summary>
    public void CheckForLostDevices()
    {
        var lostTimeout = TimeSpan.FromSeconds(_settings.DeviceLostTimeoutSeconds);
        var now = DateTime.UtcNow;

        foreach (var (deviceId, state) in _deviceStates.ToArray())
        {
            lock (state)
            {
                if (state.CurrentState != ProximityState.Lost &&
                    now - state.LastSeen > lostTimeout)
                {
                    state.CurrentState = ProximityState.Lost;
                    _logger.LogWarning("Device {DeviceName} ({DeviceId}) lost - no signal for {Timeout}s",
                        state.LastKnownName, deviceId, _settings.DeviceLostTimeoutSeconds);

                    ProximityChanged?.Invoke(this, new ProximityEvent
                    {
                        DeviceId = deviceId,
                        DeviceName = state.LastKnownName,
                        State = ProximityState.Lost,
                        Rssi = state.LastRssi,
                        SmoothedRssi = state.SmoothedRssi,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }
    }

    /// <summary>
    /// Get current state for a specific device.
    /// </summary>
    public ProximityState GetDeviceState(string deviceId)
    {
        return _deviceStates.TryGetValue(deviceId, out var state)
            ? state.CurrentState
            : ProximityState.Lost;
    }

    /// <summary>
    /// Get all current device states for UI display.
    /// </summary>
    public IReadOnlyDictionary<string, (ProximityState State, double SmoothedRssi, short LastRssi, DateTime LastSeen)> GetAllStates()
    {
        return _deviceStates.ToArray().ToDictionary(
            kvp => kvp.Key,
            kvp => (kvp.Value.CurrentState, kvp.Value.SmoothedRssi, kvp.Value.LastRssi, kvp.Value.LastSeen));
    }

    private ProximityState DetermineState(DeviceState state, double smoothedRssi)
    {
        var now = DateTime.UtcNow;

        // Hysteresis logic - use different thresholds for lock vs unlock
        if (smoothedRssi < _settings.LockRssiThreshold)
        {
            // Signal is weak - might need to lock
            if (state.CurrentState == ProximityState.Present || state.CurrentState == ProximityState.Uncertain)
            {
                // Start tracking how long signal has been weak
                state.WeakSignalStart ??= now;

                if (now - state.WeakSignalStart.Value >= TimeSpan.FromSeconds(_settings.LockDelaySeconds))
                {
                    state.WeakSignalStart = null;
                    state.StrongSignalStart = null;
                    return ProximityState.Away;
                }
                return ProximityState.Uncertain;
            }
            return state.CurrentState;
        }
        else if (smoothedRssi > _settings.UnlockRssiThreshold)
        {
            // Signal is strong - device is near
            state.WeakSignalStart = null;

            if (state.CurrentState == ProximityState.Away || state.CurrentState == ProximityState.Lost || state.CurrentState == ProximityState.Uncertain)
            {
                state.StrongSignalStart ??= now;

                if (now - state.StrongSignalStart.Value >= TimeSpan.FromSeconds(_settings.UnlockDelaySeconds))
                {
                    state.StrongSignalStart = null;
                    return ProximityState.Present;
                }
                return ProximityState.Uncertain;
            }
            return ProximityState.Present;
        }
        else
        {
            // Signal is in the dead zone between thresholds
            // Don't change state - this prevents oscillation
            return state.CurrentState == ProximityState.Lost ? ProximityState.Uncertain : state.CurrentState;
        }
    }

    private DeviceState CreateDeviceState(string deviceId)
    {
        ISignalFilter filter = _settings.FilterType switch
        {
            SignalFilterType.Kalman => new KalmanFilterAdapter(
                new KalmanFilter(_settings.KalmanProcessNoise, _settings.KalmanMeasurementNoise)),
            SignalFilterType.MovingAverage => new MovingAverageFilterAdapter(
                new MovingAverageFilter(_settings.MovingAverageWindowSize)),
            _ => new PassthroughFilter()
        };

        return new DeviceState
        {
            DeviceId = deviceId,
            Filter = filter,
            CurrentState = ProximityState.Lost
        };
    }

    public void Dispose()
    {
        _deviceStates.Clear();
    }

    private class DeviceState
    {
        public string DeviceId { get; set; } = string.Empty;
        public string LastKnownName { get; set; } = string.Empty;
        public ISignalFilter Filter { get; set; } = new PassthroughFilter();
        public ProximityState CurrentState { get; set; } = ProximityState.Lost;
        public short LastRssi { get; set; }
        public double SmoothedRssi { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime? WeakSignalStart { get; set; }
        public DateTime? StrongSignalStart { get; set; }
    }
}

/// <summary>
/// Interface for signal filters to allow swapping between Kalman, MovingAverage, etc.
/// </summary>
public interface ISignalFilter
{
    double Update(double measurement);
}

public class KalmanFilterAdapter : ISignalFilter
{
    private readonly KalmanFilter _filter;
    public KalmanFilterAdapter(KalmanFilter filter) => _filter = filter;
    public double Update(double measurement) => _filter.Update(measurement);
}

public class MovingAverageFilterAdapter : ISignalFilter
{
    private readonly MovingAverageFilter _filter;
    public MovingAverageFilterAdapter(MovingAverageFilter filter) => _filter = filter;
    public double Update(double measurement) => _filter.Update(measurement);
}

public class PassthroughFilter : ISignalFilter
{
    public double Update(double measurement) => measurement;
}
