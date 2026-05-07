namespace ProximityD.Filters;

/// <summary>
/// Simple 1D Kalman filter for RSSI signal smoothing.
/// Provides optimal estimation of the true RSSI value given noisy measurements.
/// </summary>
public class KalmanFilter
{
    private double _estimate;
    private double _errorEstimate;
    private double _processNoise;
    private double _measurementNoise;
    private bool _initialized;

    /// <summary>Process noise (Q). Can be changed at runtime via the Settings UI.</summary>
    public double ProcessNoise { get => _processNoise; set => _processNoise = value; }

    /// <summary>Measurement noise (R). Can be changed at runtime via the Settings UI.</summary>
    public double MeasurementNoise { get => _measurementNoise; set => _measurementNoise = value; }

    /// <summary>
    /// Creates a new Kalman filter for RSSI smoothing.
    /// </summary>
    /// <param name="processNoise">Process noise (Q) - how much we expect the signal to change between measurements. Lower = smoother. Typical: 0.5-2.0</param>
    /// <param name="measurementNoise">Measurement noise (R) - how noisy our RSSI readings are. Higher = more smoothing. Typical: 3-8</param>
    public KalmanFilter(double processNoise = 1.0, double measurementNoise = 4.0)
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

        // Adaptive process noise: if the new reading deviates from the current
        // estimate the user is probably actually moving (not just RSSI noise),
        // so temporarily inflate Q so the filter catches up quickly. Without
        // this, a low base Q kept the estimate "stuck" — moving the phone a
        // few meters away barely moved the smoothed value.
        //
        // Threshold is 2 dB (down from 4): real-world walk-away produces
        // gradual 3-5 dB drops per reading, which the old threshold missed.
        //
        // Use an *absolute* (innovation-squared) boost rather than a multiple
        // of the base Q so the response is consistent regardless of the
        // user's saved tuning parameters.
        double innovation = measurement - _estimate;
        double absInnovation = Math.Abs(innovation);
        double adaptiveProcessNoise = _processNoise;
        if (absInnovation > 2.0)
        {
            double excess = absInnovation - 2.0;
            //  3 dB step -> Q_eff ~= max(Q,  1)  -> gain ramps up
            //  6 dB step -> Q_eff ~= max(Q, 16)  -> gain ~ 0.80 (R=4)
            // 10 dB step -> Q_eff ~= max(Q, 64)  -> gain ~ 0.94
            // 15 dB step -> Q_eff ~= max(Q,169)  -> gain ~ 0.98
            adaptiveProcessNoise = Math.Max(_processNoise, excess * excess);
        }

        // Prediction step
        double predictedEstimate = _estimate;
        double predictedError = _errorEstimate + adaptiveProcessNoise;

        // Update step
        double kalmanGain = predictedError / (predictedError + _measurementNoise);
        _estimate = predictedEstimate + kalmanGain * innovation;
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
