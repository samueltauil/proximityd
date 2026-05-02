using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ProximityD.Configuration;
using ProximityD.Services;

namespace ProximityD.Tests;

public class WindowsActionServiceTests
{
    private readonly Mock<ILogger<WindowsActionService>> _loggerMock;
    private readonly AppSettings _settings;

    public WindowsActionServiceTests()
    {
        _loggerMock = new Mock<ILogger<WindowsActionService>>();
        _settings = new AppSettings
        {
            EnableAutoLock = true,
            EnableAutoUnlock = false
        };
    }

    [Fact]
    public void LockWorkstation_WhenAutoLockDisabled_ReturnsFalse()
    {
        _settings.EnableAutoLock = false;
        var service = new WindowsActionService(_loggerMock.Object, _settings);

        var result = service.LockWorkstation();

        result.Should().BeFalse();
    }

    [Fact]
    public void SignalPresenceForUnlock_WhenAutoUnlockDisabled_ReturnsFalse()
    {
        _settings.EnableAutoUnlock = false;
        var service = new WindowsActionService(_loggerMock.Object, _settings);

        var result = service.SignalPresenceForUnlock();

        result.Should().BeFalse();
    }

    [Fact]
    public void SignalPresenceForUnlock_WhenAutoUnlockEnabled_ReturnsTrue()
    {
        _settings.EnableAutoUnlock = true;
        var service = new WindowsActionService(_loggerMock.Object, _settings);

        var result = service.SignalPresenceForUnlock();

        result.Should().BeTrue();
    }

    [Fact]
    public void SignalPresenceForUnlock_RapidCalls_Throttled()
    {
        _settings.EnableAutoUnlock = true;
        var service = new WindowsActionService(_loggerMock.Object, _settings);

        var firstResult = service.SignalPresenceForUnlock();
        var secondResult = service.SignalPresenceForUnlock();

        firstResult.Should().BeTrue();
        secondResult.Should().BeFalse(); // Throttled
    }

    [Fact]
    public void LockWorkstation_FiresActionPerformedEvent()
    {
        var service = new WindowsActionService(_loggerMock.Object, _settings);
        string? eventMessage = null;
        service.ActionPerformed += (_, msg) => eventMessage = msg;

        service.LockWorkstation();

        // On non-Windows or in test environment, it simulates
        eventMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OnProximityChanged_AwayState_TriggersLock()
    {
        var service = new WindowsActionService(_loggerMock.Object, _settings);
        string? eventMessage = null;
        service.ActionPerformed += (_, msg) => eventMessage = msg;

        service.OnProximityChanged(Models.ProximityState.Away);

        eventMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OnProximityChanged_PresentState_WithAutoUnlockDisabled_DoesNothing()
    {
        _settings.EnableAutoUnlock = false;
        var service = new WindowsActionService(_loggerMock.Object, _settings);
        string? eventMessage = null;
        service.ActionPerformed += (_, msg) => eventMessage = msg;

        service.OnProximityChanged(Models.ProximityState.Present);

        eventMessage.Should().BeNull();
    }
}
