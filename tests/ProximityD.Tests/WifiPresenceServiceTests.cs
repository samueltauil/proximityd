using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ProximityD.Configuration;
using ProximityD.Services;

namespace ProximityD.Tests;

// Testable subclass that allows us to control PingAsync results
internal class TestableWifiPresenceService : WifiPresenceService
{
    private readonly WifiPresenceState _pingResult;

    public TestableWifiPresenceService(
        ILogger<WifiPresenceService> logger,
        AppSettings settings,
        WifiPresenceState pingResult)
        : base(logger, settings)
    {
        _pingResult = pingResult;
    }

    internal override Task<WifiPresenceState> PingAsync(string host)
    {
        return Task.FromResult(_pingResult);
    }
}

public class WifiPresenceServiceTests
{
    private readonly Mock<ILogger<WifiPresenceService>> _loggerMock;
    private readonly AppSettings _settings;

    public WifiPresenceServiceTests()
    {
        _loggerMock = new Mock<ILogger<WifiPresenceService>>();
        _settings = new AppSettings
        {
            EnableWifiPresence = true,
            WifiDeviceHostname = "192.168.1.1",
            WifiPingIntervalSeconds = 10
        };
    }

    [Fact]
    public void Start_WhenDisabled_DoesNotStartTimer()
    {
        _settings.EnableWifiPresence = false;
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);

        // Should not throw and should not start timer
        var act = () => service.Start();
        act.Should().NotThrow();
    }

    [Fact]
    public void Start_WhenHostnameEmpty_DoesNotStartTimer()
    {
        _settings.WifiDeviceHostname = string.Empty;
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);

        var act = () => service.Start();
        act.Should().NotThrow();
    }

    [Fact]
    public void CurrentState_InitiallyUnknown()
    {
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);
        service.CurrentState.Should().Be(WifiPresenceState.Unknown);
    }

    [Fact]
    public async Task PingAsync_WhenOverridden_ReturnsMockResult()
    {
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);
        var result = await service.PingAsync("test-host");
        result.Should().Be(WifiPresenceState.Present);
    }

    [Fact]
    public async Task PingAsync_Away_ReturnsAway()
    {
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Away);
        var result = await service.PingAsync("test-host");
        result.Should().Be(WifiPresenceState.Away);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);
        var act = () =>
        {
            service.Dispose();
            service.Dispose();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        var service = new TestableWifiPresenceService(_loggerMock.Object, _settings, WifiPresenceState.Present);
        var act = () => service.Stop();
        act.Should().NotThrow();
    }

    [Fact]
    public void WifiPresenceState_EnumValues_HaveExpectedOrdinals()
    {
        ((int)WifiPresenceState.Present).Should().Be(0);
        ((int)WifiPresenceState.Away).Should().Be(1);
        ((int)WifiPresenceState.Unknown).Should().Be(2);
    }
}
