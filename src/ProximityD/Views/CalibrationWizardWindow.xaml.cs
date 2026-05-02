using System.Windows;
using ProximityD.ViewModels;

namespace ProximityD.Views;

public partial class CalibrationWizardWindow : Window
{
    private readonly EventHandler _wizardClosedHandler;

    public CalibrationWizardWindow(CalibrationWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Store the handler so it can be unsubscribed when the window closes,
        // preventing the singleton ViewModel from keeping a reference to the closed window.
        _wizardClosedHandler = (_, _) => Close();
        viewModel.WizardClosed += _wizardClosedHandler;

        Closed += (_, _) => viewModel.WizardClosed -= _wizardClosedHandler;
    }
}
