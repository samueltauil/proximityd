# ProximityD

[![CI](https://github.com/samueltauil/proximityd/actions/workflows/ci.yml/badge.svg)](https://github.com/samueltauil/proximityd/actions/workflows/ci.yml)
[![Build](https://github.com/samueltauil/proximityd/actions/workflows/build.yml/badge.svg)](https://github.com/samueltauil/proximityd/actions/workflows/build.yml)
[![Release](https://github.com/samueltauil/proximityd/actions/workflows/release.yml/badge.svg)](https://github.com/samueltauil/proximityd/actions/workflows/release.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A modern Windows Bluetooth proximity detection application inspired by [BlueProximity](https://blueproximity.sourceforge.net/). Automatically locks your PC when your Bluetooth device (phone, watch, etc.) moves out of range, and optionally signals readiness to unlock when it returns.

## Features

- **BLE (Bluetooth Low Energy) Scanning** — Continuously monitors paired Bluetooth devices using WinRT APIs
- **Kalman Filter Signal Processing** — Smooth, reliable distance estimation that handles RSSI noise and fluctuation
- **Hysteresis-based State Machine** — Separate lock/unlock thresholds with time delays prevent false triggers
- **Auto-Lock** — Automatically locks your workstation when your device goes out of range
- **Presence Signaling** — Notifies when device returns (full auto-unlock requires Windows Hello)
- **System Tray App** — Runs quietly in the background with minimal resource usage
- **Multi-Device Support** — Track multiple Bluetooth devices simultaneously
- **Calibration Wizard** — Step-by-step guided threshold calibration based on your real environment
- **Signal Strength Graph** — Real-time RSSI visualization with raw and smoothed signal lines
- **WiFi Presence Detection** — Hybrid BLE + network ping for more reliable presence detection
- **Distance Estimation** — Converts RSSI to approximate meters using the log-distance path loss model
- **Windows Hello Notification** — System tray notification prompts authentication when device returns
- **Configurable Thresholds** — Fine-tune sensitivity to your specific environment
- **Event Logging** — Full audit trail of proximity events for debugging

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│                       ProximityD                          │
├──────────────────────────────────────────────────────────┤
│  UI Layer (WPF)                                           │
│  ├── System Tray Icon + Notifications                    │
│  ├── Main Window (Devices, Settings, Signal Graph, Log)  │
│  └── Calibration Wizard                                  │
├──────────────────────────────────────────────────────────┤
│  Service Layer                                            │
│  ├── ProximityBackgroundService (orchestrator)            │
│  ├── BleScanner (WinRT BLE advertisements)               │
│  ├── ProximityEngine (state machine + filters)           │
│  ├── WindowsActionService (lock/unlock)                  │
│  ├── WifiPresenceService (ping-based secondary presence) │
│  ├── UwbPresenceService (stub — future UWB support)      │
│  └── NotificationService (tray balloon notifications)    │
├──────────────────────────────────────────────────────────┤
│  Signal Processing                                        │
│  ├── KalmanFilter                                        │
│  ├── MovingAverageFilter                                 │
│  └── PathLossDistanceEstimator                           │
├──────────────────────────────────────────────────────────┤
│  Configuration                                            │
│  └── AppSettings (JSON persistence)                      │
└──────────────────────────────────────────────────────────┘
```

## Requirements

- Windows 10/11 (Build 22621 or later recommended)
- .NET 8.0 Runtime
- Bluetooth 4.0+ adapter (BLE support required)
- A paired Bluetooth device (phone, smartwatch, etc.)

## Installation

Download the latest release from the [Releases](https://github.com/samueltauil/proximityd/releases) page and extract `ProximityD-vX.X.X-win-x64.zip`. Run `ProximityD.exe` — no installer required. The app will appear in your system tray.

## Building from Source

```bash
# Clone the repository
git clone https://github.com/samueltauil/proximityd.git
cd proximityd

# Run tests (cross-platform)
dotnet test tests/ProximityD.Tests/ProximityD.Tests.csproj

# Build the app (Windows only — requires WPF)
dotnet build src/ProximityD/ProximityD.csproj

# Run
dotnet run --project src/ProximityD/ProximityD.csproj

# Publish self-contained single-file executable
dotnet publish src/ProximityD/ProximityD.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

## How It Works

### Signal Processing

Raw Bluetooth RSSI (Received Signal Strength Indicator) is inherently noisy. ProximityD uses a **1D Kalman filter** to produce smooth, reliable distance estimates:

1. **Raw RSSI** → Noisy signal from BLE advertisement
2. **Kalman Filter** → Optimal estimate of true signal strength
3. **Path Loss Model** → Optional conversion to distance in meters
4. **Hysteresis Logic** → State determination with separate thresholds

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

### WiFi Hybrid Presence

When enabled, a secondary WiFi check pings a configured hostname or IP address. This reduces false locks when BLE signal is temporarily weak (e.g., device orientation, body blocking):

- BLE says **Away** but WiFi ping **succeeds** → state remains **Uncertain**
- Both BLE and WiFi say **Away** → lock triggers

### Security Considerations

- **Auto-lock**: Uses `LockWorkStation()` Win32 API — safe and reliable
- **Auto-unlock**: Disabled by default. Windows intentionally restricts programmatic unlock for security. When enabled, ProximityD shows a system tray notification prompting Windows Hello authentication.
- **Fail-safe**: If uncertain about proximity state, the system does NOT unlock
- See [docs/CredentialProvider.md](docs/CredentialProvider.md) for information on building a Custom Windows Credential Provider for true silent auto-unlock.

## Configuration

Settings are stored in `%LOCALAPPDATA%\ProximityD\settings.json`:

### Core Settings

| Setting | Default | Description |
|---------|---------|-------------|
| `ScanIntervalMs` | 2000 | How often to check signal (ms) |
| `LockRssiThreshold` | -75 | RSSI below this → device is far |
| `UnlockRssiThreshold` | -65 | RSSI above this → device is near |
| `LockDelaySeconds` | 10 | Seconds below threshold before locking |
| `UnlockDelaySeconds` | 5 | Seconds above threshold before unlocking |
| `DeviceLostTimeoutSeconds` | 30 | Seconds of no signal before device is considered lost |
| `EnableAutoLock` | true | Automatically lock workstation |
| `EnableAutoUnlock` | false | Signal presence for unlock |
| `FilterType` | Kalman | Signal filter (`Kalman` / `MovingAverage` / `None`) |

### Distance Estimation

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableDistanceMode` | false | Display estimated distance in meters |
| `TxPowerDbm` | -59 | Bluetooth TX power at 1 meter (calibrate for your device) |
| `PathLossExponent` | 2.0 | Environment factor: 2.0 = open space, 3.0 = indoor with obstacles |

### WiFi Presence

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableWifiPresence` | false | Enable secondary WiFi/network presence check |
| `WifiDeviceHostname` | `""` | Hostname or IP to ping for presence |
| `WifiPingIntervalSeconds` | 10 | How often to ping (seconds) |

### Notifications

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableWindowsHelloNotification` | true | Show tray notification when device returns |
| `NotificationTimeoutSeconds` | 10 | How long the notification stays visible |

## Calibration

Use the built-in **Calibration Wizard** (click *Calibrate* in the Devices tab) for best results:

1. **Near step** — hold your device at your typical desk/working distance and collect 20+ RSSI samples
2. **Away step** — move to the distance where you want locking to trigger and collect 20+ samples
3. **Apply** — the wizard recommends thresholds and applies them to your settings

Manual tips:
- **Adjust lock threshold** if getting false locks while seated (try -80 dBm)
- **Increase lock delay** if you briefly step away and don't want to lock (try 15–20 s)
- **Test with your specific device** — signal strength varies between phone models
- **Consider your environment** — walls and obstacles attenuate Bluetooth signals significantly

## Known Limitations

- Bluetooth RSSI is inherently variable — no proximity system based on signal strength alone is 100% reliable
- Windows does not allow silent programmatic unlock (by design for security)
- Some BLE devices may not broadcast advertisements when the screen is off
- Signal strength can vary significantly based on device orientation and body blocking
- UWB support is stubbed — no public Windows UWB scanning API is currently available for third-party apps
- Custom Credential Provider (true silent auto-unlock) requires a separate native COM component; see [docs/CredentialProvider.md](docs/CredentialProvider.md)

## Tech Stack

- **Language**: C# (.NET 8.0)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Bluetooth**: WinRT `Windows.Devices.Bluetooth.Advertisement` APIs
- **Signal Graphing**: OxyPlot.Wpf
- **Signal Processing**: Kalman filter, Moving Average filter, Log-Distance Path Loss model
- **Architecture**: MVVM with dependency injection (`Microsoft.Extensions.Hosting`)
- **Logging**: Serilog (file-based, rolling daily)

## Roadmap

- [x] Calibration wizard UI
- [x] Signal strength graph visualization
- [x] WiFi presence detection (hybrid approach)
- [x] Windows Hello notification on device return
- [x] Distance estimation via path loss model
- [ ] UWB (Ultra-Wideband) support — pending public Windows UWB API
- [ ] Machine learning-based adaptive distance estimation
- [ ] Custom Windows Credential Provider for true silent auto-unlock (see [docs/CredentialProvider.md](docs/CredentialProvider.md))

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, coding standards, and PR guidelines.

## License

MIT License — see [LICENSE](LICENSE) for details.

## Acknowledgments

Inspired by [BlueProximity](https://blueproximity.sourceforge.net/) — a Linux Bluetooth proximity detection tool originally developed by Lars Friedrichs.
