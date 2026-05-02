using FluentAssertions;
using ProximityD.Filters;

namespace ProximityD.Tests;

public class MovingAverageFilterTests
{
    [Fact]
    public void Update_SingleValue_ReturnsThatValue()
    {
        var filter = new MovingAverageFilter(windowSize: 5);

        var result = filter.Update(-60.0);

        result.Should().Be(-60.0);
    }

    [Fact]
    public void Update_MultipleValues_ReturnsAverage()
    {
        var filter = new MovingAverageFilter(windowSize: 5);

        filter.Update(-60.0);
        filter.Update(-70.0);
        var result = filter.Update(-80.0);

        result.Should().BeApproximately(-70.0, 0.001);
    }

    [Fact]
    public void Update_ExceedsWindow_DropsOldestValues()
    {
        var filter = new MovingAverageFilter(windowSize: 3);

        filter.Update(-60.0);
        filter.Update(-70.0);
        filter.Update(-80.0);
        var result = filter.Update(-90.0); // Should drop -60

        result.Should().BeApproximately(-80.0, 0.001); // Average of -70, -80, -90
    }

    [Fact]
    public void SampleCount_TracksCorrectly()
    {
        var filter = new MovingAverageFilter(windowSize: 5);

        filter.Update(-60.0);
        filter.Update(-70.0);

        filter.SampleCount.Should().Be(2);
    }

    [Fact]
    public void SampleCount_DoesNotExceedWindowSize()
    {
        var filter = new MovingAverageFilter(windowSize: 3);

        for (int i = 0; i < 10; i++)
        {
            filter.Update(-60.0 - i);
        }

        filter.SampleCount.Should().Be(3);
    }

    [Fact]
    public void CurrentAverage_WithNoSamples_ReturnsZero()
    {
        var filter = new MovingAverageFilter(windowSize: 5);

        filter.CurrentAverage.Should().Be(0);
    }

    [Fact]
    public void Reset_ClearsAllSamples()
    {
        var filter = new MovingAverageFilter(windowSize: 5);
        filter.Update(-60.0);
        filter.Update(-70.0);

        filter.Reset();

        filter.SampleCount.Should().Be(0);
        filter.CurrentAverage.Should().Be(0);
    }
}
