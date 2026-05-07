using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Models;

namespace ProximityD.Services;

/// <summary>
/// Handles Windows-specific actions: locking/unlocking the workstation.
/// </summary>
public class WindowsActionService
{
    private readonly ILogger<WindowsActionService> _logger;
    private readonly AppSettings _settings;
    private readonly NotificationService? _notificationService;
    private DateTime _lastLockTime = DateTime.MinValue;
    private DateTime _lastUnlockAttemptTime = DateTime.MinValue;

    // Minimum time between lock actions to prevent rapid toggling
    private static readonly TimeSpan MinLockInterval = TimeSpan.FromSeconds(30);

    public event EventHandler<string>? ActionPerformed;

    public WindowsActionService(ILogger<WindowsActionService> logger, AppSettings settings, NotificationService? notificationService = null)
    {
        _logger = logger;
        _settings = settings;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Lock the Windows workstation.
    /// </summary>
    public bool LockWorkstation()
    {
        if (!_settings.EnableAutoLock)
        {
            _logger.LogDebug("Auto-lock is disabled, skipping lock action");
            return false;
        }

        // Prevent rapid re-locking
        if (DateTime.UtcNow - _lastLockTime < MinLockInterval)
        {
            _logger.LogDebug("Lock action throttled - last lock was less than {Interval}s ago", MinLockInterval.TotalSeconds);
            return false;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var result = NativeMethods.LockWorkStation();
                if (result)
                {
                    _lastLockTime = DateTime.UtcNow;
                    _logger.LogInformation("Workstation locked successfully");
                    ActionPerformed?.Invoke(this, "Workstation locked");
                    return true;
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogError("Failed to lock workstation. Win32 error: {Error}", error);
                }
            }
            else
            {
                _logger.LogInformation("[Simulated] Workstation would be locked");
                _lastLockTime = DateTime.UtcNow;
                ActionPerformed?.Invoke(this, "[Simulated] Workstation locked");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while locking workstation");
        }

        return false;
    }

    /// <summary>
    /// Attempt to signal presence for unlock.
    /// Wakes the display (if asleep) so Windows Hello can authenticate the user,
    /// then shows a notification as a fallback/reminder.
    /// </summary>
    public bool SignalPresenceForUnlock()
    {
        if (!_settings.EnableAutoUnlock)
        {
            _logger.LogDebug("Auto-unlock is disabled");
            return false;
        }

        // Prevent rapid unlock attempts
        if (DateTime.UtcNow - _lastUnlockAttemptTime < MinLockInterval)
        {
            return false;
        }

        _lastUnlockAttemptTime = DateTime.UtcNow;

        _logger.LogInformation("Device returned - waking display and signaling presence for Windows Hello.");

        // Wake the display so Windows Hello (face/fingerprint) can trigger
        WakeDisplay();

        ActionPerformed?.Invoke(this, "Device detected - waking display for Windows Hello");

        if (_settings.EnableWindowsHelloNotification)
        {
            ShowWindowsHelloNotification();
        }

        return true;
    }

    /// <summary>
    /// Wake the display from sleep/off state by simulating a small mouse movement.
    /// This causes the lock screen to appear so Windows Hello can authenticate.
    /// </summary>
    private void WakeDisplay()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            // Simulate a tiny mouse move (1px then back) to wake the display
            // without visibly moving the cursor. This is the standard technique
            // used by BLE proximity unlock utilities on Windows.
            var inputs = new NativeMethods.INPUT[2];

            inputs[0].type = NativeMethods.INPUT_MOUSE;
            inputs[0].mi.dx = 1;
            inputs[0].mi.dy = 0;
            inputs[0].mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;

            inputs[1].type = NativeMethods.INPUT_MOUSE;
            inputs[1].mi.dx = -1;
            inputs[1].mi.dy = 0;
            inputs[1].mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE;

            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            _logger.LogDebug("Display wake signal sent via SendInput");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to wake display");
        }
    }

    /// <summary>
    /// Shows a Windows Hello notification via the NotificationService.
    /// </summary>
    public void ShowWindowsHelloNotification()
    {
        _notificationService?.Show(
            "ProximityD — Device Detected",
            "Your Bluetooth device is back in range. Authenticate with Windows Hello to unlock.",
            NotificationType.Info);
    }

    /// <summary>
    /// Handle proximity state change and execute appropriate action.
    /// </summary>
    public void OnProximityChanged(ProximityState newState)
    {
        switch (newState)
        {
            case ProximityState.Away:
            case ProximityState.Lost:
                LockWorkstation();
                break;
            case ProximityState.Present:
                SignalPresenceForUnlock();
                break;
        }
    }

    /// <summary>
    /// Native Windows API imports.
    /// </summary>
    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LockWorkStation();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
