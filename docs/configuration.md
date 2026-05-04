# Configuration

Settings are stored in `%LOCALAPPDATA%\ProximityD\settings.json`.

## Core Settings

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

## Distance Estimation

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableDistanceMode` | false | Display estimated distance in meters |
| `TxPowerDbm` | -59 | Bluetooth TX power at 1 meter (calibrate for your device) |
| `PathLossExponent` | 2.0 | Environment factor: 2.0 = open space, 3.0 = indoor with obstacles |

## WiFi Presence

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableWifiPresence` | false | Enable secondary WiFi/network presence check |
| `WifiDeviceHostname` | `""` | Hostname or IP to ping for presence |
| `WifiPingIntervalSeconds` | 10 | How often to ping (seconds) |

## Notifications

| Setting | Default | Description |
|---------|---------|-------------|
| `EnableWindowsHelloNotification` | true | Show tray notification when device returns |
| `NotificationTimeoutSeconds` | 10 | How long the notification stays visible |

## Calibration

Use the built-in **Calibration Wizard** (click *Calibrate* in the Devices tab) for best results:

1. **Near step** — hold your device at your typical desk/working distance and collect 20+ RSSI samples
2. **Away step** — move to the distance where you want locking to trigger and collect 20+ samples
3. **Apply** — the wizard recommends thresholds and applies them to your settings

### Manual Tuning Tips

- **Adjust lock threshold** if getting false locks while seated (try -80 dBm)
- **Increase lock delay** if you briefly step away and don't want to lock (try 15–20 s)
- **Test with your specific device** — signal strength varies between phone models
- **Consider your environment** — walls and obstacles attenuate Bluetooth signals significantly
