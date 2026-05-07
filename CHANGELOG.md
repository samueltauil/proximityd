# Changelog

All notable changes to ProximityD will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.0.2] - 2025-06-10

### Fixed
- **App freeze**: replaced `Dispatcher.Invoke()` with `BeginInvoke()` in `MainViewModel` to prevent deadlocks when background services fire events while the UI thread holds a lock
- **Signal graph not updating**: eagerly initialize OxyPlot `PlotModel` in constructor so data renders from the first BLE reading, instead of lazily on first XAML binding access
- **BLE watcher build error**: removed invalid `IDisposable` cast of `BluetoothLEAdvertisementWatcher` in discovery cleanup
- **BLE watcher resource leak**: discovery watcher objects (`BluetoothLEAdvertisementWatcher`, `DeviceWatcher`) are now disposed after each scan cycle
- **Event handler leak**: `MainViewModel.Cleanup()` unsubscribes all event handlers on window close
- **Thread safety**: `BleScanner._isScanning` field is now `volatile` for correct cross-thread visibility

### Changed
- **Faster lock detection**: reduced `LockDelaySeconds` default from 10 to 3 seconds
- **Adaptive Kalman filter**: lowered innovation threshold from 4 dB to 2 dB for faster response to movement
- **Display wake on unlock**: `WindowsActionService` now wakes the display (simulated mouse move) and dismisses the lock screen cover (simulated Enter key) before presenting the credential prompt
- **Kalman tuning UI**: added real-time Process Noise (Q) and Measurement Noise (R) sliders in the Settings tab with live filter reconfiguration

### Added
- Global exception handlers (`DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`) that write crash logs to `%LocalAppData%\ProximityD\logs\`
- `KalmanProcessNoise` and `KalmanMeasurementNoise` settings in `AppSettings`

### Docs
- Updated default values in README, configuration, and architecture docs
- Updated unlock behavior description to reflect display wake + credential prompt

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
