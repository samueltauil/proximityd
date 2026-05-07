namespace ProximityD.Filters;

/// <summary>
/// Estimates physical distance from RSSI using the log-distance path loss model.
/// distance = 10 ^ ((txPower - rssi) / (10 * pathLossExponent))
/// </summary>
public class PathLossDistanceEstimator
{
    private double _txPowerDbm;
    private double _pathLossExponent;

    /// <summary>
    /// Initializes a new instance of PathLossDistanceEstimator.
    /// </summary>
    /// <param name="txPowerDbm">RSSI at 1 meter (typically -59 dBm for Bluetooth).</param>
    /// <param name="pathLossExponent">Environment factor: 2.0 = free space, 2.7-3.5 = indoor.</param>
    public PathLossDistanceEstimator(double txPowerDbm = -59.0, double pathLossExponent = 2.0)
    {
        if (pathLossExponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pathLossExponent), "Path loss exponent must be positive.");
        }

        _txPowerDbm = txPowerDbm;
        _pathLossExponent = pathLossExponent;
    }

    /// <summary>Gets the configured TX power (RSSI at 1 meter).</summary>
    public double TxPowerDbm => _txPowerDbm;

    /// <summary>Gets the configured path loss exponent.</summary>
    public double PathLossExponent => _pathLossExponent;

    /// <summary>
    /// Updates the calibration parameters. Used after the calibration wizard
    /// captures a per-device reference RSSI so the distance display reflects
    /// real hardware rather than the generic -59 dBm default.
    /// </summary>
    public void Configure(double txPowerDbm, double pathLossExponent)
    {
        if (pathLossExponent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pathLossExponent), "Path loss exponent must be positive.");
        }
        _txPowerDbm = txPowerDbm;
        _pathLossExponent = pathLossExponent;
    }

    /// <summary>
    /// Estimates distance in meters from an RSSI reading.
    /// </summary>
    /// <param name="rssi">Measured RSSI in dBm.</param>
    /// <returns>Estimated distance in meters. Returns 0.01 minimum.</returns>
    public double EstimateDistance(double rssi)
    {
        var distance = Math.Pow(10.0, (_txPowerDbm - rssi) / (10.0 * _pathLossExponent));
        return Math.Max(0.01, distance);
    }

    /// <summary>
    /// Estimates the expected RSSI at a given distance.
    /// </summary>
    /// <param name="distanceMeters">Distance in meters. Must be positive.</param>
    /// <returns>Expected RSSI in dBm.</returns>
    public double EstimateRssi(double distanceMeters)
    {
        if (distanceMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters), "Distance must be positive.");
        }

        return _txPowerDbm - 10.0 * _pathLossExponent * Math.Log10(distanceMeters);
    }
}
