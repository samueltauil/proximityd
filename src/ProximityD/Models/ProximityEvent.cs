namespace ProximityD.Models;

/// <summary>
/// Represents a proximity event triggered by signal changes.
/// </summary>
public class ProximityEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public ProximityState State { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public double Rssi { get; set; }
    public double SmoothedRssi { get; set; }
}

public enum ProximityState
{
    /// <summary>Device is within range - user is present</summary>
    Present,

    /// <summary>Device is out of range - user has left</summary>
    Away,

    /// <summary>Device signal is uncertain</summary>
    Uncertain,

    /// <summary>Device has not been seen for extended period</summary>
    Lost
}
