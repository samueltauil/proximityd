using System.Drawing;
using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProximityD.Configuration;
using ProximityD.Filters;
using ProximityD.Services;
using ProximityD.ViewModels;
using ProximityD.Views;
using Serilog;
using Serilog.Events;

namespace ProximityD;

public partial class App : Application
{
    // Stable, app-specific identifier. Per-user (Local\) so two different Windows
    // accounts can each run their own instance.
    private const string SingleInstanceMutexName = "Local\\ProximityD.SingleInstance.Mutex.{4F1B2C3D-7E5A-4B9F-8A1D-2C3E4F5A6B7C}";
    private const string SingleInstanceSignalName = "Local\\ProximityD.SingleInstance.Signal.{4F1B2C3D-7E5A-4B9F-8A1D-2C3E4F5A6B7C}";

    private static System.Threading.Mutex? s_singleInstanceMutex;
    private System.Threading.EventWaitHandle? _showSignal;
    private System.Threading.CancellationTokenSource? _showSignalCts;

    private IHost? _host;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private AppSettings _settings = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance enforcement: try to acquire a named mutex. If another
        // instance already owns it, signal that instance to surface its window
        // and exit immediately.
        s_singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                if (System.Threading.EventWaitHandle.TryOpenExisting(SingleInstanceSignalName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
                // best-effort; even if signaling fails we still must not start a second instance.
            }

            // Release our handle on the mutex (we never owned it) and shut down.
            s_singleInstanceMutex.Dispose();
            s_singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // We are the primary instance; set up a wait handle that secondary
        // launches will set to ask us to bring the window forward.
        _showSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, SingleInstanceSignalName);
        _showSignalCts = new System.Threading.CancellationTokenSource();
        _ = System.Threading.Tasks.Task.Run(() => WaitForShowRequestsAsync(_showSignalCts.Token));

        var settings = AppSettings.Load();
        _settings = settings;

        // Map settings LogLevel to Serilog level
        var logLevel = settings.LogLevel?.ToLowerInvariant() switch
        {
            "verbose" or "trace" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.File(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ProximityD", "logs", "proximityd-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(settings);
                services.AddSingleton<BleScanner>();
                services.AddSingleton<ProximityEngine>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<WindowsActionService>();
                services.AddSingleton<WifiPresenceService>();
                services.AddSingleton<UwbPresenceService>();
                services.AddSingleton<PathLossDistanceEstimator>(sp =>
                {
                    var s = sp.GetRequiredService<AppSettings>();
                    return new PathLossDistanceEstimator(s.TxPowerDbm, s.PathLossExponent);
                });
                services.AddSingleton<SignalGraphViewModel>();
                services.AddSingleton<CalibrationWizardViewModel>();
                services.AddSingleton<ProximityBackgroundService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddHostedService(sp => sp.GetRequiredService<ProximityBackgroundService>());
            })
            .Build();

        await _host.StartAsync();

        // Wire up notification events
        var notificationService = _host.Services.GetRequiredService<NotificationService>();
        notificationService.NotificationRequested += OnNotificationRequested;

        // Create main window
        _mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // Setup system tray
        SetupTrayIcon();

        // Show or minimize based on settings
        if (!settings.StartMinimized)
        {
            _mainWindow.Show();
        }
    }

    private void OnNotificationRequested(object? sender, NotificationRequest e)
    {
        if (!_settings.ShowNotifications)
        {
            return;
        }

        // Build a simple custom balloon so the configured NotificationTimeoutSeconds is respected.
        var balloon = new System.Windows.Controls.Border
        {
            Background = System.Windows.Media.Brushes.WhiteSmoke,
            BorderBrush = System.Windows.Media.Brushes.Gray,
            BorderThickness = new System.Windows.Thickness(1),
            CornerRadius = new System.Windows.CornerRadius(4),
            Padding = new System.Windows.Thickness(10),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = $"{e.Title}\n{e.Message}",
                TextWrapping = System.Windows.TextWrapping.Wrap,
                MaxWidth = 280
            }
        };

        _trayIcon?.ShowCustomBalloon(
            balloon,
            System.Windows.Controls.Primitives.PopupAnimation.Slide,
            _settings.NotificationTimeoutSeconds * 1000);
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            Icon = LoadAppIcon(),
            ToolTipText = "ProximityD - Bluetooth Proximity Detection",
            MenuActivation = PopupActivationMode.RightClick
        };

        // Create context menu
        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show" };
        showItem.Click += (_, _) => _mainWindow?.ShowFromTray();
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayMouseDoubleClick += (_, _) => _mainWindow?.ShowFromTray();
    }

    private async void ExitApplication()
    {
        _mainWindow?.AllowClose();
        _trayIcon?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        ReleaseSingleInstanceHandles();
        Log.CloseAndFlush();
        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        ReleaseSingleInstanceHandles();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void ReleaseSingleInstanceHandles()
    {
        try { _showSignalCts?.Cancel(); } catch { }
        try { _showSignal?.Set(); } catch { }
        _showSignalCts?.Dispose();
        _showSignalCts = null;
        _showSignal?.Dispose();
        _showSignal = null;

        if (s_singleInstanceMutex != null)
        {
            try { s_singleInstanceMutex.ReleaseMutex(); } catch { }
            s_singleInstanceMutex.Dispose();
            s_singleInstanceMutex = null;
        }
    }

    private void WaitForShowRequestsAsync(System.Threading.CancellationToken token)
    {
        var handle = _showSignal;
        if (handle == null)
        {
            return;
        }

        var waitHandles = new System.Threading.WaitHandle[] { handle, token.WaitHandle };
        while (!token.IsCancellationRequested)
        {
            int signaled;
            try
            {
                signaled = System.Threading.WaitHandle.WaitAny(waitHandles);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (signaled != 0)
            {
                return;
            }

            // Marshal to UI thread and surface the main window.
            Dispatcher.BeginInvoke(new Action(() => _mainWindow?.ShowFromTray()));
        }
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
            {
                return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load tray icon from resources, falling back to system icon");
        }

        return SystemIcons.Application;
    }
}

