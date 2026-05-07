using System.ComponentModel;
using System.Windows;
using ProximityD.ViewModels;

namespace ProximityD.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // Provide a UI prompt for pairings that require a PIN to be typed on the PC
        // (some Android devices and legacy Bluetooth pairings).
        _viewModel.SetPinPromptHandler(PromptForPinAsync);

        // Hook the pair-flow UI: launches Windows Bluetooth settings + shows clear steps,
        // then re-runs Discover so paired status updates.
        _viewModel.SetPairFlowHandler(ShowPairInstructionsAsync);
    }

    private Task<bool> ShowPairInstructionsAsync(string deviceName, string deviceId)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:bluetooth",
                    UseShellExecute = true
                });
            }
            catch
            {
                // user can open it manually from the dialog
            }

            var dialog = new PairInstructionsWindow(deviceName, deviceId) { Owner = this };
            return dialog.ShowDialog() == true;
        }).Task;
    }

    private Task<string?> PromptForPinAsync(string deviceAddress)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            var dialog = new PinPromptWindow(deviceAddress) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.Pin : null;
        }).Task;
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        OpenCalibrationWizard();
    }

    private void OpenCalibrationWizard()
    {
        // Reset the singleton ViewModel so every wizard session starts fresh.
        _viewModel.CalibrationWizard.Reset();

        // Populate the device picker with currently-tracked PAIRED devices. Calibration
        // requires a real signal stream, which only paired devices reliably provide,
        // and the user already manages pairing on the Devices tab.
        _viewModel.CalibrationWizard.SetAvailableDevices(
            _viewModel.TrackedDevices
                .Where(d => d.IsPaired)
                .Select(d => new CalibrationDeviceOption
                {
                    DeviceId = d.DeviceId,
                    DeviceName = d.DeviceName
                }));

        if (_viewModel.CalibrationWizard.AvailableDevices.Count == 0)
        {
            MessageBox.Show(this,
                "No paired devices available. Open the Devices tab, click \"Discover Devices\", pair a phone, then try again.",
                "Calibration",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var wizard = new CalibrationWizardWindow(_viewModel.CalibrationWizard);
        wizard.Owner = this;
        _viewModel.CalibrationWizard.ThresholdsApplied += OnThresholdsApplied;
        _viewModel.CalibrationWizard.WizardClosed += OnWizardClosed;
        wizard.ShowDialog();
    }

    private void OnThresholdsApplied(object? sender, ThresholdRecommendation recommendation)
    {
        _viewModel.LockThreshold = recommendation.LockThreshold;
        _viewModel.UnlockThreshold = recommendation.UnlockThreshold;
        if (recommendation.ReferenceRssiAtNear < 0)
        {
            _viewModel.ApplyTxPowerReference(recommendation.ReferenceRssiAtNear);
        }
        _viewModel.SaveSettingsCommand.Execute(null);
        UnsubscribeWizardEvents();
    }

    private void OnWizardClosed(object? sender, EventArgs e)
    {
        UnsubscribeWizardEvents();
    }

    private void UnsubscribeWizardEvents()
    {
        _viewModel.CalibrationWizard.ThresholdsApplied -= OnThresholdsApplied;
        _viewModel.CalibrationWizard.WizardClosed -= OnWizardClosed;
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // Minimize to system tray
            Hide();
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Allow close during application shutdown
        if (_allowClose || Application.Current.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        // Hide instead of closing (let tray icon handle exit)
        e.Cancel = true;
        Hide();
    }

    /// <summary>
    /// Allow the window to close (called during app exit).
    /// </summary>
    public void AllowClose()
    {
        _allowClose = true;
        _viewModel.Cleanup();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}

