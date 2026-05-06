using System.Collections.ObjectModel;
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

    // Per-advertisement-address sample buckets for the current Start..Stop window.
    // Used to auto-lock onto the user's phone even when iOS rotates its BLE address.
    private readonly Dictionary<string, List<double>> _bucketedSamples = new();
    private const int MinBucketSamplesToLock = 3;

    // Safety margin below near mean to set the unlock threshold
    private const double UnlockSafetyMargin = 10.0;
    // Small buffer above away mean to set the lock threshold
    private const double LockTriggerMargin = 5.0;
    // Half-gap applied when thresholds need to be corrected
    private const double ThresholdHalfGap = 5.0;

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

    /// <summary>
    /// Diagnostic status shown while the wizard collects samples (e.g. how many
    /// distinct addresses have been seen, how many samples are bucketed under
    /// the dominant one). Helps the user confirm their phone is actually being
    /// heard, especially with iOS rotating BLE addresses.
    /// </summary>
    [ObservableProperty]
    private string _collectionStatus = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyThresholdsCommand))]
    private bool _hasCalibrationData;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCollectingCommand))]
    private string _selectedDeviceId = string.Empty;

    /// <summary>
    /// Available devices the user can calibrate against. Populated by MainViewModel
    /// before the wizard window is shown.
    /// </summary>
    public ObservableCollection<CalibrationDeviceOption> AvailableDevices { get; } = new();

    /// <summary>
    /// Replaces the AvailableDevices list with the given options.
    /// </summary>
    public void SetAvailableDevices(IEnumerable<CalibrationDeviceOption> devices)
    {
        AvailableDevices.Clear();
        foreach (var d in devices)
        {
            AvailableDevices.Add(d);
        }
    }

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

    [RelayCommand(CanExecute = nameof(CanStartCollecting))]
    public void StartCollecting()
    {
        // The wizard's content panel doesn't render a per-step screen, so users
        // typically click Start without first walking Next/Next. Auto-advance from
        // Welcome / SelectDevice into NearCalibration so OnRssiReading actually
        // accumulates samples (it filters by CurrentStep).
        if (CurrentStep == WizardStep.Welcome || CurrentStep == WizardStep.SelectDevice)
        {
            CurrentStep = WizardStep.NearCalibration;
            UpdateInstructionText();
        }
        else if (CurrentStep == WizardStep.Results)
        {
            // Restart calibration from the beginning when the user clicks Start again
            // after seeing results.
            CurrentStep = WizardStep.NearCalibration;
            UpdateInstructionText();
        }

        if (CurrentStep == WizardStep.NearCalibration)
        {
            _nearSamples.Clear();
        }
        else if (CurrentStep == WizardStep.AwayCalibration)
        {
            _awaySamples.Clear();
        }

        _bucketedSamples.Clear();
        SampleCount = 0;
        CollectionStatus = "Listening for advertisements...";
        IsCollectingSamples = true;
    }

    private bool CanStartCollecting() => !string.IsNullOrWhiteSpace(SelectedDeviceId);

    [RelayCommand]
    public void StopCollecting()
    {
        IsCollectingSamples = false;

        // Prefer the bucketed (per-advertisement-address) path when we have any
        // data — it auto-locks to the closest device and is robust to iOS BLE
        // privacy. Fall back to the legacy single-stream samples path if no
        // bucketed data exists (unit tests that drive OnRssiReading(double)
        // directly without an address still work).
        if (_bucketedSamples.Count > 0)
        {
            var bestBucket = _bucketedSamples
                .Where(kv => kv.Value.Count >= MinBucketSamplesToLock)
                .OrderByDescending(kv => Median(kv.Value))
                .FirstOrDefault();

            if (bestBucket.Value == null || bestBucket.Value.Count == 0)
            {
                CollectionStatus =
                    $"Not enough samples from any single device ({_bucketedSamples.Count} addresses seen). " +
                    "Hold your phone closer and try Start again.";
                return;
            }

            var samples = bestBucket.Value;
            CollectionStatus =
                $"Locked onto {bestBucket.Key} ({samples.Count} samples, " +
                $"median {Median(samples):F1} dBm). " +
                $"Total addresses seen: {_bucketedSamples.Count}.";

            if (CurrentStep == WizardStep.NearCalibration)
            {
                _nearSamples.Clear();
                _nearSamples.AddRange(samples);
            }
            else if (CurrentStep == WizardStep.AwayCalibration)
            {
                _awaySamples.Clear();
                _awaySamples.AddRange(samples);
            }
        }

        // Auto-advance through the steps so the user just clicks Start/Stop twice
        // (once near, once away) without navigating Next manually.
        if (CurrentStep == WizardStep.NearCalibration && _nearSamples.Count > 0)
        {
            CurrentStep = WizardStep.AwayCalibration;
            UpdateInstructionText();
        }
        else if (CurrentStep == WizardStep.AwayCalibration && _nearSamples.Count > 0 && _awaySamples.Count > 0)
        {
            CalculateRecommendations();
            CurrentStep = WizardStep.Results;
            UpdateInstructionText();
        }
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    [RelayCommand(CanExecute = nameof(HasCalibrationData))]
    public void ApplyThresholds()
    {
        // Guard: never apply zero thresholds that result from an uncompleted calibration run.
        if (!HasCalibrationData)
        {
            return;
        }

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

        if (!IsCollectingSamples)
        {
            return;
        }

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

    /// <summary>
    /// Per-advertisement reading. Buckets samples by address so the wizard can
    /// auto-lock to the closest device on Stop. This handles iOS BLE privacy
    /// (rotating Random Resolvable Private Addresses) where the address on
    /// inbound adverts never matches the stored identity address from pairing.
    /// </summary>
    public void OnRssiReading(string deviceId, double rssi)
    {
        CurrentRssi = rssi;

        if (!IsCollectingSamples)
        {
            return;
        }

        if (CurrentStep != WizardStep.NearCalibration && CurrentStep != WizardStep.AwayCalibration)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        if (!_bucketedSamples.TryGetValue(deviceId, out var bucket))
        {
            bucket = new List<double>();
            _bucketedSamples[deviceId] = bucket;
        }
        bucket.Add(rssi);

        // Surface live progress: total samples across all addresses, plus the
        // current best (closest) candidate.
        var totalSamples = 0;
        foreach (var b in _bucketedSamples.Values) totalSamples += b.Count;
        SampleCount = totalSamples;

        var best = _bucketedSamples
            .Where(kv => kv.Value.Count >= MinBucketSamplesToLock)
            .OrderByDescending(kv => Median(kv.Value))
            .FirstOrDefault();
        CollectionStatus = best.Value != null && best.Value.Count > 0
            ? $"Best so far: {best.Key} ({best.Value.Count} samples, median {Median(best.Value):F1} dBm). Addresses seen: {_bucketedSamples.Count}, total samples: {totalSamples}."
            : $"Listening... addresses seen: {_bucketedSamples.Count}, total samples: {totalSamples}.";
    }

    /// <summary>Gets a read-only copy of the near samples (for testing).</summary>
    public IReadOnlyList<double> NearSamples => _nearSamples.AsReadOnly();

    /// <summary>Gets a read-only copy of the away samples (for testing).</summary>
    public IReadOnlyList<double> AwaySamples => _awaySamples.AsReadOnly();

    private void CalculateRecommendations()
    {
        if (_nearSamples.Count == 0 || _awaySamples.Count == 0)
        {
            return;
        }

        var nearMean = _nearSamples.Average();
        var awayMean = _awaySamples.Average();

        // Unlock threshold: near mean minus safety margin (so device must be close to unlock)
        RecommendedUnlockThreshold = nearMean - UnlockSafetyMargin;
        // Lock threshold: away mean plus a small trigger margin (trigger lock before fully away)
        RecommendedLockThreshold = awayMean + LockTriggerMargin;

        // Sanity check: lock threshold should always be more negative (weaker signal) than unlock.
        // If near and away samples overlap, split the midpoint evenly.
        if (RecommendedLockThreshold > RecommendedUnlockThreshold)
        {
            var mid = (RecommendedLockThreshold + RecommendedUnlockThreshold) / 2.0;
            RecommendedLockThreshold = mid - ThresholdHalfGap;
            RecommendedUnlockThreshold = mid + ThresholdHalfGap;
        }

        HasCalibrationData = true;
    }

    /// <summary>
    /// Resets the wizard to the initial state so it can be used for a fresh calibration session.
    /// Should be called before re-showing the wizard dialog.
    /// </summary>
    public void Reset()
    {
        IsCollectingSamples = false;
        _nearSamples.Clear();
        _awaySamples.Clear();
        _bucketedSamples.Clear();
        SampleCount = 0;
        RecommendedLockThreshold = 0;
        RecommendedUnlockThreshold = 0;
        HasCalibrationData = false;
        SelectedDeviceId = string.Empty;
        CollectionStatus = string.Empty;
        CurrentStep = WizardStep.Welcome;
        UpdateInstructionText();
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

/// <summary>An option in the calibration device picker.</summary>
public class CalibrationDeviceOption
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public override string ToString() =>
        string.IsNullOrWhiteSpace(DeviceName) ? DeviceId : $"{DeviceName}  ({DeviceId})";
}
