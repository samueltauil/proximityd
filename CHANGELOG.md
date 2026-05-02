# Changelog

All notable changes to ProximityD will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `PathLossDistanceEstimator` — converts RSSI readings to estimated distances using the log-distance path loss model
- `WifiPresenceService` — secondary presence detection via network ping
- `UwbPresenceService` — stub for future UWB (Ultra-Wideband) support
- `NotificationService` — decoupled system tray notification service
- `CalibrationWizardViewModel` — guides users through RSSI threshold calibration
- `SignalGraphViewModel` — real-time signal strength graph with 60-second rolling window
- Distance display in status panel (meters)
- Signal Graph tab with OxyPlot chart
- Calibrate button in Devices tab
- CalibrationWizardWindow UI
- GitHub Actions CI/CD workflows (ci, build, release, lint)
- Root-level solution file `ProximityD.sln`
- `.editorconfig` for consistent code style
- `CONTRIBUTING.md` and `CHANGELOG.md`
- `docs/CredentialProvider.md` — research notes on Windows Credential Provider integration

### Changed
- `AppSettings` — added 8 new configuration properties:
  - `TxPowerDbm`, `PathLossExponent`, `EnableDistanceMode`
  - `EnableWifiPresence`, `WifiDeviceHostname`, `WifiPingIntervalSeconds`
  - `EnableWindowsHelloNotification`, `NotificationTimeoutSeconds`
- `WindowsActionService` — now accepts optional `NotificationService` and shows Windows Hello notification on device return
- `MainViewModel` — added `DistanceMeters`, `SignalGraph`, `CalibrationWizard` properties
- `App.xaml.cs` — registers new services in DI container; wires up notification events

## [1.0.0] - Initial Release

### Added
- BLE scanning via WinRT `Windows.Devices.Bluetooth.Advertisement` APIs
- Kalman filter and Moving Average filter for RSSI smoothing
- Hysteresis-based proximity state machine (Present/Away/Uncertain/Lost)
- Auto-lock via `LockWorkStation()` Win32 API
- Presence signaling for Windows Hello auto-unlock
- System tray icon with context menu
- Settings persistence (JSON in `%LOCALAPPDATA%\ProximityD\`)
- Multi-device tracking
- Event log with 100-entry rolling buffer
- Serilog file logging with daily rolling
