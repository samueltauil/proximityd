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
    /// When true (default), the first state transition observed for each
    /// device is suppressed so the app does not fire lock/unlock on launch
    /// based on a cold filter or a single advertisement. Pattern borrowed
    /// from BlueProximity (ignoreFirstTransition).
    /// </summary>
    public bool SuppressFirstTransition { get; set; } = true;

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

                // Suppress the very first state transition after startup: the
                // filter is still warming up, advert cadence may be irregular,
                // and the user shouldn't see a spurious lock or unlock just
                // because the app launched. Pattern borrowed from
                // BlueProximity (ignoreFirstTransition).
                if (SuppressFirstTransition && state.IgnoreFirstTransition)
                {
                    state.IgnoreFirstTransition = false;
                    _logger.LogInformation(
                        "Device {DeviceName} ({DeviceId}) first transition {OldState} -> {NewState} suppressed (RSSI: {Rssi}, Smoothed: {Smoothed:F1})",
                        deviceName, deviceId, previousState, newState, rssi, smoothedRssi);
                    return newState;
                }

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
                    state.CommittedState = ProximityState.Lost;
                    state.WeakSignalStart = null;
                    state.StrongSignalStart = null;
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

        // Use the last *committed* state (Present/Away/Lost) as the hysteresis
        // anchor. The transient `Uncertain` returned mid-transition must not
        // overwrite it, otherwise a brief excursion past the lock threshold
        // followed by readings in the dead zone would strand the device in
        // Uncertain forever and the user would see "Unknown/Uncertain" until
        // the signal eventually crossed the unlock threshold again.
        var committed = state.CommittedState;

        if (smoothedRssi < _settings.LockRssiThreshold)
        {
            // Weak signal: candidate to lock. Cancel any pending unlock.
            state.StrongSignalStart = null;

            if (committed != ProximityState.Away)
            {
                state.WeakSignalStart ??= now;
                if (now - state.WeakSignalStart.Value >= TimeSpan.FromSeconds(_settings.LockDelaySeconds))
                {
                    state.WeakSignalStart = null;
                    state.CommittedState = ProximityState.Away;
                    return ProximityState.Away;
                }
                return ProximityState.Uncertain;
            }
            return ProximityState.Away;
        }
        else if (smoothedRssi > _settings.UnlockRssiThreshold)
        {
            // Strong signal: candidate to unlock. Cancel any pending lock.
            state.WeakSignalStart = null;

            if (committed != ProximityState.Present)
            {
                state.StrongSignalStart ??= now;
                if (now - state.StrongSignalStart.Value >= TimeSpan.FromSeconds(_settings.UnlockDelaySeconds))
                {
                    state.StrongSignalStart = null;
                    state.CommittedState = ProximityState.Present;
                    return ProximityState.Present;
                }
                return ProximityState.Uncertain;
            }
            return ProximityState.Present;
        }
        else
        {
            // Dead zone between thresholds. CRITICAL: do NOT reset the matching
            // pending timer here — natural RSSI oscillation around either
            // threshold would otherwise restart the countdown on every bounce
            // and the lock/unlock would never trigger. Only cancel the
            // *opposing* candidacy (a transient excursion deep into the other
            // zone is what should reset things), and leave the in-progress
            // pending timer intact so it can complete on the next reading
            // back across the threshold.
            //
            // Stay anchored to the last committed state (Present/Away) so the
            // UI doesn't flicker to Uncertain on every dead-zone sample.
            return committed == ProximityState.Lost ? ProximityState.Uncertain : committed;
        }
    }

    /// <summary>
    /// Reconfigure Kalman filter parameters on all live device filters.
    /// Called when the user changes noise sliders in the Settings UI.
    /// </summary>
    public void ReconfigureFilters(double processNoise, double measurementNoise)
    {
        foreach (var state in _deviceStates.Values)
        {
            if (state.Filter is KalmanFilterAdapter kalman)
            {
                kalman.Reconfigure(processNoise, measurementNoise);
            }
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

        /// <summary>
        /// Last *committed* (non-transitional) state — only ever Present, Away,
        /// or Lost. Used by <see cref="DetermineState"/> as the hysteresis
        /// anchor so transient Uncertain readings do not strand the device.
        /// </summary>
        public ProximityState CommittedState { get; set; } = ProximityState.Lost;

        public short LastRssi { get; set; }
        public double SmoothedRssi { get; set; }
        public DateTime LastSeen { get; set; }
        public DateTime? WeakSignalStart { get; set; }
        public DateTime? StrongSignalStart { get; set; }

        /// <summary>
        /// True until the first state transition for this device has been
        /// observed and suppressed. Prevents firing lock/unlock on app start
        /// based on a cold filter or a single advertisement.
        /// </summary>
        public bool IgnoreFirstTransition { get; set; } = true;
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

    public void Reconfigure(double processNoise, double measurementNoise)
    {
        _filter.ProcessNoise = processNoise;
        _filter.MeasurementNoise = measurementNoise;
    }
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
