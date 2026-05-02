namespace ProximityD.Models;

/// <summary>
/// Represents a tracked Bluetooth device with its current signal information.
/// </summary>
public class TrackedDevice
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public short LastRssi { get; set; }
    public double SmoothedRssi { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.MinValue;
    public bool IsInRange { get; set; }
    public DeviceType Type { get; set; } = DeviceType.BluetoothLE;
}

public enum DeviceType
{
    BluetoothClassic,
    BluetoothLE
}
