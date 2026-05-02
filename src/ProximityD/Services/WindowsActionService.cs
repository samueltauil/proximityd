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
    private DateTime _lastLockTime = DateTime.MinValue;
    private DateTime _lastUnlockAttemptTime = DateTime.MinValue;

    // Minimum time between lock actions to prevent rapid toggling
    private static readonly TimeSpan MinLockInterval = TimeSpan.FromSeconds(30);

    public event EventHandler<string>? ActionPerformed;

    public WindowsActionService(ILogger<WindowsActionService> logger, AppSettings settings)
    {
        _logger = logger;
        _settings = settings;
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
    /// Note: Windows does not allow silent programmatic unlock for security reasons.
    /// This method can trigger Windows Hello or provide a notification.
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

        _logger.LogInformation("Device returned - signaling presence. User may need to authenticate via Windows Hello.");
        ActionPerformed?.Invoke(this, "Device detected - ready to unlock via Windows Hello");

        // On Windows, we cannot programmatically unlock.
        // Options for the user:
        // 1. Windows Hello (facial recognition/fingerprint) will auto-trigger
        // 2. We could implement a custom Credential Provider (advanced)
        // 3. Show a notification that device is back in range

        return true;
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
    }
}
