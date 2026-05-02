# ProximityD

A modern Windows Bluetooth proximity detection application inspired by [BlueProximity](https://blueproximity.sourceforge.net/). Automatically locks your PC when your Bluetooth device (phone, watch, etc.) moves out of range, and optionally signals readiness to unlock when it returns.

## Features

- **BLE (Bluetooth Low Energy) Scanning** — Continuously monitors paired Bluetooth devices using WinRT APIs
- **Kalman Filter Signal Processing** — Smooth, reliable distance estimation that handles RSSI noise and fluctuation
- **Hysteresis-based State Machine** — Separate lock/unlock thresholds with time delays prevent false triggers
- **Auto-Lock** — Automatically locks your workstation when your device goes out of range
- **Presence Signaling** — Notifies when device returns (full auto-unlock requires Windows Hello)
- **System Tray App** — Runs quietly in the background with minimal resource usage
- **Multi-Device Support** — Track multiple Bluetooth devices simultaneously
- **Configurable Thresholds** — Calibrate sensitivity to your environment
- **Event Logging** — Full audit trail of proximity events for debugging

## Architecture

```
┌─────────────────────────────────────────────────┐
│                    ProximityD                     │
├─────────────────────────────────────────────────┤
│  UI Layer (WPF)                                  │
│  ├── System Tray Icon                           │
│  ├── Main Window (Settings, Devices, Log)       │
│  └── Calibration Wizard                         │
├─────────────────────────────────────────────────┤
│  Service Layer                                   │
│  ├── ProximityBackgroundService (orchestrator)   │
│  ├── BleScanner (WinRT BLE advertisements)      │
│  ├── ProximityEngine (state machine + filters)  │
│  └── WindowsActionService (lock/unlock)         │
├─────────────────────────────────────────────────┤
│  Signal Processing                               │
│  ├── Kalman Filter                              │
│  └── Moving Average Filter                      │
├─────────────────────────────────────────────────┤
│  Configuration                                   │
│  └── AppSettings (JSON persistence)             │
└─────────────────────────────────────────────────┘
```

## Requirements

- Windows 10/11 (Build 22621 or later recommended)
- .NET 8.0 Runtime
- Bluetooth 4.0+ adapter (BLE support required)
- A paired Bluetooth device (phone, smartwatch, etc.)

## Building

```bash
# Clone the repository
git clone https://github.com/samueltauil/proximityd.git
cd proximityd/src/ProximityD

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run
```

## How It Works

### Signal Processing

Raw Bluetooth RSSI (Received Signal Strength Indicator) is inherently noisy. ProximityD uses a **1D Kalman filter** to produce smooth, reliable distance estimates:

1. **Raw RSSI** → Noisy signal from BLE advertisement
2. **Kalman Filter** → Optimal estimate of true signal strength
3. **Hysteresis Logic** → State determination with separate thresholds

### Proximity State Machine

```
                    ┌──────────┐
                    │   Lost   │ (no signal)
                    └────┬─────┘
                         │ signal detected
                         ▼
    ┌───────────┐   ┌──────────┐   ┌──────────┐
    │  Present  │◄──│Uncertain │──►│   Away   │
    └───────────┘   └──────────┘   └──────────┘
     (strong signal) (transitioning) (weak signal)
         │                               │
         │      ┌────────────┐           │
         └─────►│ Lock/Unlock│◄──────────┘
                └────────────┘
```

### Hysteresis (Anti-Oscillation)

To prevent false lock/unlock cycles:

- **Lock triggers** when smoothed RSSI drops below `-75 dBm` for **10 seconds** continuously
- **Unlock triggers** when smoothed RSSI rises above `-65 dBm` for **5 seconds** continuously
- The **10 dBm gap** between thresholds prevents rapid toggling

### Security Considerations

- **Auto-lock**: Uses `LockWorkStation()` Win32 API — safe and reliable
- **Auto-unlock**: Disabled by default. Windows intentionally restricts programmatic unlock for security. When enabled, ProximityD signals device presence to trigger Windows Hello authentication.
- **Fail-safe**: If uncertain about proximity state, the system does NOT unlock

## Configuration

Settings are stored in `%LOCALAPPDATA%\ProximityD\settings.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `ScanIntervalMs` | 2000 | How often to check signal (ms) |
| `LockRssiThreshold` | -75 | RSSI below this → device is far |
| `UnlockRssiThreshold` | -65 | RSSI above this → device is near |
| `LockDelaySeconds` | 10 | Seconds below threshold before locking |
| `UnlockDelaySeconds` | 5 | Seconds above threshold before unlocking |
| `EnableAutoLock` | true | Automatically lock workstation |
| `EnableAutoUnlock` | false | Signal presence for unlock |
| `FilterType` | Kalman | Signal filter (Kalman/MovingAverage/None) |

## Calibration Tips

1. **Start with defaults** — they work well for most environments
2. **Adjust lock threshold** if getting false locks while seated (try -80 dBm)
3. **Increase lock delay** if you briefly step away and don't want to lock (try 15-20s)
4. **Test with your specific device** — signal strength varies between phone models
5. **Consider environment** — walls and obstacles attenuate Bluetooth signals

## Known Limitations

- Bluetooth RSSI is inherently variable — no proximity system based on signal strength alone is 100% reliable
- Windows does not allow silent programmatic unlock (by design for security)
- Some BLE devices may not broadcast advertisements when the screen is off
- Signal strength can vary significantly based on device orientation and body blocking

## Tech Stack

- **Language**: C# (.NET 8.0)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Bluetooth**: WinRT `Windows.Devices.Bluetooth.Advertisement` APIs
- **Signal Processing**: Custom Kalman filter implementation
- **Architecture**: MVVM with dependency injection
- **Logging**: Serilog (file-based, rolling daily)

## Future Improvements

- [ ] Calibration wizard UI
- [ ] Signal strength graph visualization
- [ ] WiFi presence detection (hybrid approach)
- [ ] Windows Hello companion device integration
- [ ] UWB (Ultra-Wideband) support when available
- [ ] Machine learning-based distance estimation
- [ ] Custom Windows Credential Provider for true auto-unlock

## License

MIT License - see [LICENSE](LICENSE) for details.

## Acknowledgments

Inspired by [BlueProximity](https://blueproximity.sourceforge.net/) — a Linux Bluetooth proximity detection tool originally developed by Lars Friedrichs.
