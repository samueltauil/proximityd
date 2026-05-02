using System.ComponentModel;
using System.Windows;
using ProximityD.ViewModels;

namespace ProximityD.Views;

public partial class MainWindow : Window
{
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
        // Hide instead of closing (let tray icon handle exit)
        e.Cancel = true;
        Hide();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
