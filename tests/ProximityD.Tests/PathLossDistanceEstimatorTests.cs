using FluentAssertions;
using ProximityD.Filters;

namespace ProximityD.Tests;

public class PathLossDistanceEstimatorTests
{
    [Fact]
    public void Constructor_DefaultParameters_SetsCorrectValues()
    {
        var estimator = new PathLossDistanceEstimator();
        estimator.TxPowerDbm.Should().Be(-59.0);
        estimator.PathLossExponent.Should().Be(2.0);
    }

    [Fact]
    public void Constructor_CustomParameters_SetsCorrectValues()
    {
        var estimator = new PathLossDistanceEstimator(-65.0, 3.0);
        estimator.TxPowerDbm.Should().Be(-65.0);
        estimator.PathLossExponent.Should().Be(3.0);
    }

    [Fact]
    public void Constructor_NegativePathLossExponent_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PathLossDistanceEstimator(pathLossExponent: -1.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ZeroPathLossExponent_ThrowsArgumentOutOfRangeException()
    {
        var act = () => new PathLossDistanceEstimator(pathLossExponent: 0.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EstimateDistance_AtTxPower_ReturnsOneM()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var distance = estimator.EstimateDistance(-59.0);
        distance.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void EstimateDistance_BelowTxPower_ReturnsMoreThanOneM()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var distance = estimator.EstimateDistance(-69.0);
        distance.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void EstimateDistance_VeryWeakSignal_ReturnsLargeDistance()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var distance = estimator.EstimateDistance(-100.0);
        distance.Should().BeGreaterThan(10.0);
    }

    [Fact]
    public void EstimateDistance_AlwaysReturnsPositive()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var distance = estimator.EstimateDistance(-10.0); // stronger than tx power
        distance.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void EstimateDistance_ReturnsMinimumOf0_01()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var distance = estimator.EstimateDistance(0.0); // extremely strong signal
        distance.Should().BeGreaterThanOrEqualTo(0.01);
    }

    [Fact]
    public void EstimateRssi_AtOneM_ReturnsTxPower()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var rssi = estimator.EstimateRssi(1.0);
        rssi.Should().BeApproximately(-59.0, 0.001);
    }

    [Fact]
    public void EstimateRssi_AtTenM_IsWeakerThanAtOneM()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var rssiAt1m = estimator.EstimateRssi(1.0);
        var rssiAt10m = estimator.EstimateRssi(10.0);
        rssiAt10m.Should().BeLessThan(rssiAt1m);
    }

    [Fact]
    public void EstimateRssi_ZeroDistance_ThrowsArgumentOutOfRangeException()
    {
        var estimator = new PathLossDistanceEstimator();
        var act = () => estimator.EstimateRssi(0.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EstimateRssi_NegativeDistance_ThrowsArgumentOutOfRangeException()
    {
        var estimator = new PathLossDistanceEstimator();
        var act = () => estimator.EstimateRssi(-5.0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RoundTrip_EstimateDistance_ThenEstimateRssi_ReturnsOriginal()
    {
        var estimator = new PathLossDistanceEstimator(-59.0, 2.0);
        var originalRssi = -75.0;
        var distance = estimator.EstimateDistance(originalRssi);
        var recoveredRssi = estimator.EstimateRssi(distance);
        recoveredRssi.Should().BeApproximately(originalRssi, 0.001);
    }
}
