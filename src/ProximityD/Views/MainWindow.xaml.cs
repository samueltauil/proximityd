using System.ComponentModel;
using System.Windows;
using ProximityD.ViewModels;

namespace ProximityD.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
