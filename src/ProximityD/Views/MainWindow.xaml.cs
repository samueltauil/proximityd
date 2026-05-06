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
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}

