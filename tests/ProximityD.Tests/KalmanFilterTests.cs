using FluentAssertions;
using ProximityD.Filters;

namespace ProximityD.Tests;

public class KalmanFilterTests
{
    [Fact]
    public void Update_FirstMeasurement_ReturnsInputValue()
    {
        var filter = new KalmanFilter();

        var result = filter.Update(-60.0);

        result.Should().Be(-60.0);
    }

    [Fact]
    public void Update_SubsequentMeasurements_SmoothsOutput()
    {
        var filter = new KalmanFilter(processNoise: 0.1, measurementNoise: 10.0);

        filter.Update(-60.0);
        var result = filter.Update(-80.0);

        // Should be between the two values, closer to first due to smoothing
        result.Should().BeGreaterThan(-80.0);
        result.Should().BeLessThan(-60.0);
    }

    [Fact]
    public void Update_StableSignal_ConvergesToValue()
    {
        var filter = new KalmanFilter(processNoise: 0.1, measurementNoise: 10.0);

        double result = 0;
        for (int i = 0; i < 100; i++)
        {
            result = filter.Update(-65.0);
        }

        result.Should().BeApproximately(-65.0, 0.1);
    }

    [Fact]
    public void Update_NoisySignal_ReducesVariance()
    {
        var filter = new KalmanFilter(processNoise: 0.1, measurementNoise: 10.0);
        var random = new Random(42);
        var outputs = new List<double>();

        for (int i = 0; i < 50; i++)
        {
            var noisyValue = -65.0 + (random.NextDouble() * 20 - 10); // ±10 noise
            outputs.Add(filter.Update(noisyValue));
        }

        // Last 20 outputs should have less variance than ±10
        var lastOutputs = outputs.Skip(30).ToList();
        var variance = lastOutputs.Select(x => Math.Pow(x - lastOutputs.Average(), 2)).Average();
        variance.Should().BeLessThan(30.0); // Much less than input variance of ~33
    }

    [Fact]
    public void CurrentEstimate_AfterUpdates_ReturnsLastEstimate()
    {
        var filter = new KalmanFilter();

        filter.Update(-60.0);
        filter.Update(-65.0);

        filter.CurrentEstimate.Should().NotBe(0);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var filter = new KalmanFilter();
        filter.Update(-60.0);
        filter.Update(-65.0);

        filter.Reset();
        var result = filter.Update(-80.0);

        // After reset, first measurement should be returned as-is
        result.Should().Be(-80.0);
    }

    [Fact]
    public void HighMeasurementNoise_ProducesMoreSmoothing()
    {
        var lowNoise = new KalmanFilter(processNoise: 0.1, measurementNoise: 2.0);
        var highNoise = new KalmanFilter(processNoise: 0.1, measurementNoise: 50.0);

        lowNoise.Update(-60.0);
        highNoise.Update(-60.0);

        var lowResult = lowNoise.Update(-80.0);
        var highResult = highNoise.Update(-80.0);

        // High measurement noise filter should stay closer to previous estimate
        Math.Abs(highResult - (-60.0)).Should().BeLessThan(Math.Abs(lowResult - (-60.0)));
    }
}
