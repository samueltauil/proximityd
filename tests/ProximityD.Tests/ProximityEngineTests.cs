using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ProximityD.Configuration;
using ProximityD.Models;
using ProximityD.Services;

namespace ProximityD.Tests;

public class ProximityEngineTests
{
    private readonly Mock<ILogger<ProximityEngine>> _loggerMock;
    private readonly AppSettings _settings;

    public ProximityEngineTests()
    {
        _loggerMock = new Mock<ILogger<ProximityEngine>>();
        _settings = new AppSettings
        {
            LockRssiThreshold = -75,
            UnlockRssiThreshold = -65,
            LockDelaySeconds = 0, // Instant for testing
            UnlockDelaySeconds = 0, // Instant for testing
            DeviceLostTimeoutSeconds = 5,
            FilterType = SignalFilterType.None, // No filtering for predictable tests
            KalmanProcessNoise = 0.1,
            KalmanMeasurementNoise = 10.0
        };
    }

    [Fact]
    public void ProcessReading_FirstReading_StartsInLostState()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // Initial state before any reading
        var state = engine.GetDeviceState("device-1");

        state.Should().Be(ProximityState.Lost);
    }

    [Fact]
    public void ProcessReading_StrongSignal_TransitionsToPresent()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // With 0 delay, strong signal from Lost state transitions directly to Present
        var state = engine.ProcessReading("device-1", "Phone", -50);

        state.Should().Be(ProximityState.Present);
    }

    [Fact]
    public void ProcessReading_WeakSignal_TransitionsToAway()
    {
        _settings.LockDelaySeconds = 0;
        _settings.UnlockDelaySeconds = 0;
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // First get device to Present state
        engine.ProcessReading("device-1", "Phone", -50);

        // Now send weak signal - with 0 delay, transitions directly to Away
        var state = engine.ProcessReading("device-1", "Phone", -85);
        state.Should().Be(ProximityState.Away);
    }

    [Fact]
    public void ProcessReading_SignalInDeadZone_MaintainsState()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // Get to Present state
        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-1", "Phone", -50);

        // Signal in dead zone (-75 to -65) should not change state
        var state = engine.ProcessReading("device-1", "Phone", -70);
        state.Should().Be(ProximityState.Present);
    }

    [Fact]
    public void ProcessReading_MultipleDevices_IndependentStates()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-2", "Watch", -85);

        engine.GetDeviceState("device-1").Should().Be(ProximityState.Present);
        engine.GetDeviceState("device-2").Should().NotBe(ProximityState.Present);
    }

    [Fact]
    public void ProcessReading_RaisesProximityChangedEvent()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);
        var events = new List<ProximityEvent>();
        engine.ProximityChanged += (_, e) => events.Add(e);

        engine.ProcessReading("device-1", "Phone", -50);

        events.Should().HaveCount(1);
        events[0].DeviceId.Should().Be("device-1");
        events[0].DeviceName.Should().Be("Phone");
        events[0].State.Should().Be(ProximityState.Present);
    }

    [Fact]
    public void CheckForLostDevices_DeviceNotSeenForTimeout_MarksAsLost()
    {
        _settings.DeviceLostTimeoutSeconds = 0; // Instant timeout for test
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);
        var events = new List<ProximityEvent>();
        engine.ProximityChanged += (_, e) => events.Add(e);

        // Get to Uncertain state
        engine.ProcessReading("device-1", "Phone", -50);

        // Wait a moment then check - device should be lost
        Thread.Sleep(50);
        engine.CheckForLostDevices();

        var lostEvent = events.LastOrDefault(e => e.State == ProximityState.Lost);
        lostEvent.Should().NotBeNull();
        lostEvent!.DeviceId.Should().Be("device-1");
        lostEvent.DeviceName.Should().Be("Phone"); // Should have device name
    }

    [Fact]
    public void CheckForLostDevices_RecentlySeenDevice_NotMarkedAsLost()
    {
        _settings.DeviceLostTimeoutSeconds = 60;
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        engine.ProcessReading("device-1", "Phone", -50);
        engine.CheckForLostDevices();

        engine.GetDeviceState("device-1").Should().NotBe(ProximityState.Lost);
    }

    [Fact]
    public void GetAllStates_ReturnsAllTrackedDevices()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-2", "Watch", -60);

        var states = engine.GetAllStates();

        states.Should().HaveCount(2);
        states.Should().ContainKey("device-1");
        states.Should().ContainKey("device-2");
    }

    [Fact]
    public void ProcessReading_WithKalmanFilter_SmoothsRssi()
    {
        _settings.FilterType = SignalFilterType.Kalman;
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);
        var events = new List<ProximityEvent>();
        engine.ProximityChanged += (_, e) => events.Add(e);

        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-1", "Phone", -80);

        // Smoothed RSSI should not jump all the way to -80
        var lastEvent = events.Last();
        lastEvent.SmoothedRssi.Should().BeGreaterThan(-80.0);
    }

    [Fact]
    public void ProcessReading_IsThreadSafe()
    {
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // Run concurrent reads/writes
        var tasks = new List<Task>();
        for (int i = 0; i < 10; i++)
        {
            var deviceId = $"device-{i}";
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    engine.ProcessReading(deviceId, $"Device {i}", (short)(-50 - j % 20));
                }
            }));
        }

        // Also run CheckForLostDevices concurrently
        tasks.Add(Task.Run(() =>
        {
            for (int j = 0; j < 50; j++)
            {
                engine.CheckForLostDevices();
                Thread.Sleep(1);
            }
        }));

        // Should not throw
        var act = () => Task.WaitAll(tasks.ToArray());
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ClearsState()
    {
        var engine = new ProximityEngine(_loggerMock.Object, _settings);
        engine.ProcessReading("device-1", "Phone", -50);

        engine.Dispose();

        engine.GetAllStates().Should().BeEmpty();
    }

    [Fact]
    public void ProcessReading_LockDelay_PreventsImmediateLock()
    {
        _settings.LockDelaySeconds = 10; // 10 second delay
        _settings.UnlockDelaySeconds = 0;
        using var engine = new ProximityEngine(_loggerMock.Object, _settings);

        // Get to Present
        engine.ProcessReading("device-1", "Phone", -50);
        engine.ProcessReading("device-1", "Phone", -50);

        // Weak signal should go to Uncertain, not Away (due to delay)
        var state = engine.ProcessReading("device-1", "Phone", -85);
        state.Should().Be(ProximityState.Uncertain);

        // Still uncertain on subsequent readings (delay hasn't elapsed)
        state = engine.ProcessReading("device-1", "Phone", -85);
        state.Should().Be(ProximityState.Uncertain);
    }
}
