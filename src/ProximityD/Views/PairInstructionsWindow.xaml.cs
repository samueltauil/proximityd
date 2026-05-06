using System.Diagnostics;
using System.Windows;

namespace ProximityD.Views;

public partial class PairInstructionsWindow : Window
{
    public PairInstructionsWindow(string deviceName, string deviceId)
    {
        InitializeComponent();
        DeviceNameText.Text = $"Device: {deviceName}  ({deviceId})";
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:bluetooth",
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore — user can open it manually
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
