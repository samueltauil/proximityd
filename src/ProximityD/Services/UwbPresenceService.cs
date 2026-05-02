namespace ProximityD.Services;

/// <summary>
/// Stub implementation for UWB (Ultra-Wideband) presence detection.
/// UWB provides centimeter-level accuracy for proximity sensing, but the required
/// Windows APIs (Windows.Devices.Uwb) are not yet publicly available as of Windows 11 22H2.
///
/// When Microsoft publishes stable UWB APIs, this service should:
/// 1. Enumerate UWB-capable devices via Windows.Devices.Enumeration
/// 2. Create a UwbSession to measure ranging data
/// 3. Subscribe to ranging reports (distance + angle-of-arrival)
/// 4. Map distance measurements to UwbPresenceState
///
/// Reference: https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/ultra-wideband
/// </summary>
public class UwbPresenceService : IDisposable
{
    /// <summary>
    /// Indicates whether UWB is supported on this system.
    /// Currently returns false — public UWB ranging APIs are not yet available in Windows SDK.
    /// </summary>
    public static bool IsUwbSupported => false;

    /// <summary>Raised when the UWB presence state changes. Currently never raised.</summary>
    public event EventHandler<UwbPresenceState>? PresenceChanged;

    /// <summary>Gets the current UWB presence state (always NotSupported).</summary>
    public UwbPresenceState CurrentState => UwbPresenceState.NotSupported;

    /// <summary>
    /// Starts UWB presence detection. Currently a no-op.
    /// </summary>
    public void Start()
    {
        // UWB ranging APIs are not yet publicly available.
        // Future implementation will enumerate UWB devices here.
    }

    /// <summary>Stops UWB presence detection.</summary>
    public void Stop()
    {
        // No-op: nothing to stop.
    }

    /// <summary>Releases resources.</summary>
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Represents the UWB-based presence state.</summary>
public enum UwbPresenceState
{
    /// <summary>Device is within range.</summary>
    Present,
    /// <summary>Device is out of range.</summary>
    Away,
    /// <summary>State could not be determined.</summary>
    Unknown,
    /// <summary>UWB is not supported on this system.</summary>
    NotSupported
}
