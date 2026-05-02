using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ProximityD.ViewModels;

/// <summary>
/// Steps of the calibration wizard.
/// </summary>
public enum WizardStep
{
    Welcome,
    SelectDevice,
    NearCalibration,
    AwayCalibration,
    Results
}

/// <summary>
/// ViewModel for the calibration wizard that guides the user through
/// collecting near and away RSSI samples to recommend optimal thresholds.
/// </summary>
public partial class CalibrationWizardViewModel : ObservableObject
{
    private readonly List<double> _nearSamples = new();
    private readonly List<double> _awaySamples = new();

    [ObservableProperty]
    private WizardStep _currentStep = WizardStep.Welcome;

    [ObservableProperty]
    private string _instructionText = "Welcome to the ProximityD Calibration Wizard. This wizard will help you find the optimal RSSI thresholds for your environment.";

    [ObservableProperty]
    private double _currentRssi;

    [ObservableProperty]
    private double _recommendedLockThreshold;

    [ObservableProperty]
    private double _recommendedUnlockThreshold;

    [ObservableProperty]
    private int _sampleCount;

    [ObservableProperty]
    private bool _isCollectingSamples;

    [ObservableProperty]
    private string _selectedDeviceId = string.Empty;

    /// <summary>Raised when the wizard requests to apply thresholds to settings.</summary>
    public event EventHandler<ThresholdRecommendation>? ThresholdsApplied;

    /// <summary>Raised when the wizard is cancelled or finished.</summary>
    public event EventHandler? WizardClosed;

    [RelayCommand]
    public void NextStep()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.Welcome => WizardStep.SelectDevice,
            WizardStep.SelectDevice => WizardStep.NearCalibration,
            WizardStep.NearCalibration => WizardStep.AwayCalibration,
            WizardStep.AwayCalibration => WizardStep.Results,
            _ => CurrentStep
        };
        UpdateInstructionText();
    }

    [RelayCommand]
    public void PreviousStep()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.SelectDevice => WizardStep.Welcome,
            WizardStep.NearCalibration => WizardStep.SelectDevice,
            WizardStep.AwayCalibration => WizardStep.NearCalibration,
            WizardStep.Results => WizardStep.AwayCalibration,
            _ => CurrentStep
        };
        UpdateInstructionText();
    }

    [RelayCommand]
    public void StartCollecting()
    {
        if (CurrentStep == WizardStep.NearCalibration)
            _nearSamples.Clear();
        else if (CurrentStep == WizardStep.AwayCalibration)
            _awaySamples.Clear();

        SampleCount = 0;
        IsCollectingSamples = true;
    }

    [RelayCommand]
    public void StopCollecting()
    {
        IsCollectingSamples = false;
        if (CurrentStep == WizardStep.AwayCalibration && _nearSamples.Count > 0 && _awaySamples.Count > 0)
        {
            CalculateRecommendations();
        }
    }

    [RelayCommand]
    public void ApplyThresholds()
    {
        ThresholdsApplied?.Invoke(this, new ThresholdRecommendation
        {
            LockThreshold = (int)Math.Round(RecommendedLockThreshold),
            UnlockThreshold = (int)Math.Round(RecommendedUnlockThreshold)
        });
        WizardClosed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Cancel()
    {
        IsCollectingSamples = false;
        WizardClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Called when a new RSSI reading is available from the ProximityEngine.
    /// Adds the reading to the active sample collection if collecting.
    /// </summary>
    public void OnRssiReading(double rssi)
    {
        CurrentRssi = rssi;

        if (!IsCollectingSamples) return;

        if (CurrentStep == WizardStep.NearCalibration)
        {
            _nearSamples.Add(rssi);
            SampleCount = _nearSamples.Count;
        }
        else if (CurrentStep == WizardStep.AwayCalibration)
        {
            _awaySamples.Add(rssi);
            SampleCount = _awaySamples.Count;
        }
    }

    /// <summary>Gets a read-only copy of the near samples (for testing).</summary>
    public IReadOnlyList<double> NearSamples => _nearSamples.AsReadOnly();

    /// <summary>Gets a read-only copy of the away samples (for testing).</summary>
    public IReadOnlyList<double> AwaySamples => _awaySamples.AsReadOnly();

    private void CalculateRecommendations()
    {
        if (_nearSamples.Count == 0 || _awaySamples.Count == 0) return;

        var nearMean = _nearSamples.Average();
        var awayMean = _awaySamples.Average();

        // Unlock threshold: near mean minus safety margin
        RecommendedUnlockThreshold = nearMean - 10.0;
        // Lock threshold: away mean plus a small trigger margin
        RecommendedLockThreshold = awayMean + 5.0;

        // Sanity: lock should always be more negative than unlock
        if (RecommendedLockThreshold > RecommendedUnlockThreshold)
        {
            var mid = (RecommendedLockThreshold + RecommendedUnlockThreshold) / 2.0;
            RecommendedLockThreshold = mid - 5.0;
            RecommendedUnlockThreshold = mid + 5.0;
        }
    }

    private void UpdateInstructionText()
    {
        InstructionText = CurrentStep switch
        {
            WizardStep.Welcome => "Welcome to the ProximityD Calibration Wizard. This wizard will help you find the optimal RSSI thresholds for your environment.",
            WizardStep.SelectDevice => "Select the Bluetooth device you want to use for proximity detection.",
            WizardStep.NearCalibration => "Hold your device at the distance you consider 'near' (e.g., at your desk). Click Start and collect at least 20 samples, then click Stop.",
            WizardStep.AwayCalibration => "Move your device to the distance you consider 'away' (e.g., across the room). Click Start and collect at least 20 samples, then click Stop.",
            WizardStep.Results => "Calibration complete. Review the recommended thresholds below and click Apply to use them.",
            _ => string.Empty
        };
    }
}

/// <summary>Contains the recommended lock/unlock thresholds from calibration.</summary>
public class ThresholdRecommendation
{
    public int LockThreshold { get; set; }
    public int UnlockThreshold { get; set; }
}
