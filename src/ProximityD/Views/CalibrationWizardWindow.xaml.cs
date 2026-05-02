using System.Windows;
using ProximityD.ViewModels;

namespace ProximityD.Views;

public partial class CalibrationWizardWindow : Window
{
    public CalibrationWizardWindow(CalibrationWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.WizardClosed += (_, _) => Close();
    }
}
