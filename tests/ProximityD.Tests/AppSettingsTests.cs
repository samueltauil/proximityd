using FluentAssertions;
using ProximityD.Configuration;

namespace ProximityD.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DefaultSettings_HaveCorrectValues()
    {
        var settings = new AppSettings();

        settings.ScanIntervalMs.Should().Be(2000);
        settings.LockRssiThreshold.Should().Be(-75);
        settings.UnlockRssiThreshold.Should().Be(-65);
        settings.LockDelaySeconds.Should().Be(3);
        settings.UnlockDelaySeconds.Should().Be(5);
        settings.DeviceLostTimeoutSeconds.Should().Be(30);
        settings.EnableAutoLock.Should().BeTrue();
        settings.EnableAutoUnlock.Should().BeFalse();
        settings.StartWithWindows.Should().BeFalse();
        settings.StartMinimized.Should().BeTrue();
        settings.FilterType.Should().Be(SignalFilterType.Kalman);
        settings.LogLevel.Should().Be("Information");
    }

    [Fact]
    public void SaveAndLoad_RoundTripsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ProximityD_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        try
        {
            var settings = new AppSettings
            {
                LockRssiThreshold = -80,
                UnlockRssiThreshold = -60,
                EnableAutoLock = false,
                EnableAutoUnlock = true,
                TrackedDevices = new List<TrackedDeviceConfig>
                {
                    new() { DeviceId = "ABC123", DeviceName = "Test Phone", MacAddress = "AA:BB:CC:DD:EE:FF", Enabled = true }
                }
            };

            // Save to temp file
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            File.WriteAllText(settingsPath, json);

            // Load from file
            var loadedJson = File.ReadAllText(settingsPath);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(loadedJson, new System.Text.Json.JsonSerializerOptions
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            loaded.Should().NotBeNull();
            loaded!.LockRssiThreshold.Should().Be(-80);
            loaded.UnlockRssiThreshold.Should().Be(-60);
            loaded.EnableAutoLock.Should().BeFalse();
            loaded.EnableAutoUnlock.Should().BeTrue();
            loaded.TrackedDevices.Should().HaveCount(1);
            loaded.TrackedDevices[0].DeviceId.Should().Be("ABC123");
            loaded.TrackedDevices[0].DeviceName.Should().Be("Test Phone");
            loaded.TrackedDevices[0].MacAddress.Should().Be("AA:BB:CC:DD:EE:FF");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        // AppSettings.Load() should return defaults when no file exists
        var settings = AppSettings.Load();

        settings.Should().NotBeNull();
        settings.LockRssiThreshold.Should().Be(-75);
    }

    [Fact]
    public void Thresholds_HaveCorrectHysteresisGap()
    {
        var settings = new AppSettings();

        // Unlock threshold should be higher (less negative) than lock threshold
        settings.UnlockRssiThreshold.Should().BeGreaterThan(settings.LockRssiThreshold);

        // The gap provides hysteresis
        var gap = settings.UnlockRssiThreshold - settings.LockRssiThreshold;
        gap.Should().BeGreaterOrEqualTo(10); // At least 10 dBm gap
    }
}
