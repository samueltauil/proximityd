namespace ProximityD.Filters;

/// <summary>
/// Simple 1D Kalman filter for RSSI signal smoothing.
/// Provides optimal estimation of the true RSSI value given noisy measurements.
/// </summary>
public class KalmanFilter
{
    private double _estimate;
    private double _errorEstimate;
    private readonly double _processNoise;
    private readonly double _measurementNoise;
    private bool _initialized;

    /// <summary>
    /// Creates a new Kalman filter for RSSI smoothing.
    /// </summary>
    /// <param name="processNoise">Process noise (Q) - how much we expect the signal to change between measurements. Lower = smoother. Typical: 0.01-1.0</param>
    /// <param name="measurementNoise">Measurement noise (R) - how noisy our RSSI readings are. Higher = more smoothing. Typical: 5-20</param>
    public KalmanFilter(double processNoise = 0.1, double measurementNoise = 10.0)
    {
        _processNoise = processNoise;
        _measurementNoise = measurementNoise;
        _errorEstimate = 1.0;
        _initialized = false;
    }

    /// <summary>
    /// Update the filter with a new RSSI measurement and get the smoothed estimate.
    /// </summary>
    public double Update(double measurement)
    {
        if (!_initialized)
        {
            _estimate = measurement;
            _errorEstimate = _measurementNoise;
            _initialized = true;
            return _estimate;
        }

        // Prediction step
        double predictedEstimate = _estimate;
        double predictedError = _errorEstimate + _processNoise;

        // Update step
        double kalmanGain = predictedError / (predictedError + _measurementNoise);
        _estimate = predictedEstimate + kalmanGain * (measurement - predictedEstimate);
        _errorEstimate = (1 - kalmanGain) * predictedError;

        return _estimate;
    }

    /// <summary>
    /// Get the current smoothed estimate without providing a new measurement.
    /// </summary>
    public double CurrentEstimate => _estimate;

    /// <summary>
    /// Reset the filter state.
    /// </summary>
    public void Reset()
    {
        _initialized = false;
        _estimate = 0;
        _errorEstimate = 1.0;
    }
}
