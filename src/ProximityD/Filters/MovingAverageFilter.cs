namespace ProximityD.Filters;

/// <summary>
/// Moving average filter for RSSI smoothing.
/// Simpler alternative to Kalman filter, useful for comparison.
/// </summary>
public class MovingAverageFilter
{
    private readonly Queue<double> _samples;
    private readonly int _windowSize;

    public MovingAverageFilter(int windowSize = 10)
    {
        _windowSize = windowSize;
        _samples = new Queue<double>(windowSize);
    }

    /// <summary>
    /// Add a new measurement and get the smoothed average.
    /// </summary>
    public double Update(double measurement)
    {
        _samples.Enqueue(measurement);
        if (_samples.Count > _windowSize)
        {
            _samples.Dequeue();
        }
        return _samples.Average();
    }

    /// <summary>
    /// Get the current average without adding a new measurement.
    /// </summary>
    public double CurrentAverage => _samples.Count > 0 ? _samples.Average() : 0;

    /// <summary>
    /// Number of samples currently in the window.
    /// </summary>
    public int SampleCount => _samples.Count;

    /// <summary>
    /// Reset the filter state.
    /// </summary>
    public void Reset()
    {
        _samples.Clear();
    }
}
