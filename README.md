<p align="center">
  <img src="assets/logo.svg" alt="ProximityD" width="160" height="160">
</p>

<h1 align="center">ProximityD</h1>

<p align="center">
  <a href="https://github.com/samueltauil/proximityd/actions/workflows/ci.yml"><img src="https://github.com/samueltauil/proximityd/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/samueltauil/proximityd/actions/workflows/build.yml"><img src="https://github.com/samueltauil/proximityd/actions/workflows/build.yml/badge.svg" alt="Build"></a>
  <a href="https://github.com/samueltauil/proximityd/actions/workflows/release.yml"><img src="https://github.com/samueltauil/proximityd/actions/workflows/release.yml/badge.svg" alt="Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
</p>

A modern Windows Bluetooth proximity detection application inspired by [BlueProximity](https://blueproximity.sourceforge.net/). Automatically locks your PC when your Bluetooth device (phone, watch, etc.) moves out of range, and wakes the display to present the credential prompt when it returns.

<p align="center">
  <img src="assets/screenshot-main.png" alt="ProximityD Main Window" width="800">
</p>

<details>
<summary>More screenshots</summary>

| Settings | Signal Graph |
|----------|-------------|
| <img src="assets/screenshot-settings.png" alt="Settings" width="400"> | <img src="assets/screenshot-signal-graph.png" alt="Signal Graph" width="400"> |

</details>

## Features

- **Auto-Lock & Presence-Triggered Unlock** — Locks your workstation when your device leaves range; wakes display and presents the credential prompt when it returns
- **BLE Scanning** — Continuous monitoring of paired Bluetooth devices via WinRT APIs
- **Adaptive Kalman Filter** — Responsive signal smoothing that tracks movement quickly while filtering jitter
- **Hysteresis State Machine** — Separate lock/unlock thresholds with time delays to prevent false triggers
- **Signal Filter Tuning** — Real-time Kalman Q/R sliders in the Settings UI
- **WiFi Hybrid Presence** — Secondary network ping reduces false locks when BLE signal is weak
- **Multi-Device Support** — Track multiple Bluetooth devices simultaneously
- **Calibration Wizard** — Guided threshold calibration based on your real environment
- **Signal Strength Graph** — Real-time RSSI visualization
- **System Tray App** — Runs quietly in the background

## Requirements

- Windows 10/11 (Build 22621 or later recommended)
- .NET 8.0 Runtime
- Bluetooth 4.0+ adapter (BLE support required)
- A paired Bluetooth device (phone, smartwatch, etc.)

## Installation

Download the latest release from the [Releases](https://github.com/samueltauil/proximityd/releases) page and extract `ProximityD-vX.X.X-win-x64.zip`. Run `ProximityD.exe` — no installer required. The app will appear in your system tray.

## Building from Source

```bash
git clone https://github.com/samueltauil/proximityd.git
cd proximityd

# Run tests (cross-platform)
dotnet test tests/ProximityD.Tests/ProximityD.Tests.csproj

# Build the app (Windows only — requires WPF)
dotnet build src/ProximityD/ProximityD.csproj

# Publish self-contained single-file executable
dotnet publish src/ProximityD/ProximityD.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/
```

## How It Works

ProximityD monitors BLE advertisement signals from your paired device, applies a Kalman filter to smooth noisy RSSI readings, and uses a hysteresis-based state machine to determine proximity. When the device is out of range long enough, the workstation locks. When it returns, a notification prompts Windows Hello authentication.

For full details on signal processing, state machine logic, and security model, see the [Architecture documentation](docs/architecture.md).

## Configuration

Settings are stored in `%LOCALAPPDATA%\ProximityD\settings.json`. Key options:

| Setting | Default | Description |
|---------|---------|-------------|
| `LockRssiThreshold` | -75 | RSSI below this → device is far |
| `UnlockRssiThreshold` | -65 | RSSI above this → device is near |
| `LockDelaySeconds` | 3 | Seconds below threshold before locking |
| `UnlockDelaySeconds` | 5 | Seconds above threshold before unlocking |
| `EnableAutoLock` | true | Automatically lock workstation |
| `EnableAutoUnlock` | false | Wake display and present credential prompt |

For the full settings reference and calibration guide, see [Configuration](docs/configuration.md).

## Known Limitations

- Bluetooth RSSI is inherently variable — no signal-strength-based proximity system is 100% reliable
- Windows does not allow silent programmatic unlock (by design for security); ProximityD wakes the display and presents the credential prompt for manual authentication
- Some BLE devices may not broadcast advertisements when the screen is off
- UWB support is stubbed — no public Windows UWB scanning API is currently available

## Documentation

| Document | Description |
|----------|-------------|
| [Architecture](docs/architecture.md) | System design, signal processing, state machine, security |
| [Configuration](docs/configuration.md) | Full settings reference and calibration guide |
| [Roadmap](docs/roadmap.md) | Planned and completed features |
| [Credential Provider](docs/CredentialProvider.md) | Custom Windows Credential Provider for silent auto-unlock |
| [Contributing](CONTRIBUTING.md) | Development setup, coding standards, PR guidelines |
| [Changelog](CHANGELOG.md) | Release history |

## License

MIT License — see [LICENSE](LICENSE) for details.

## Acknowledgments

Inspired by [BlueProximity](https://blueproximity.sourceforge.net/) — a Linux Bluetooth proximity detection tool originally developed by Lars Friedrichs.
