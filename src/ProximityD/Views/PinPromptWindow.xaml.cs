using System.Windows;

namespace ProximityD.Views;

public partial class PinPromptWindow : Window
{
    public string DeviceAddress { get; }
    public string? Pin { get; private set; }

    public PinPromptWindow(string deviceAddress)
    {
        DeviceAddress = deviceAddress;
        InitializeComponent();
        Loaded += (_, _) => PinBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Pin = PinBox.Text?.Trim();
        DialogResult = !string.IsNullOrEmpty(Pin);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Pin = null;
        DialogResult = false;
    }
}
