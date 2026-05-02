using FluentAssertions;
using ProximityD.ViewModels;

namespace ProximityD.Tests;

public class CalibrationWizardViewModelTests
{
    [Fact]
    public void Constructor_InitializesWithWelcomeStep()
    {
        var vm = new CalibrationWizardViewModel();
        vm.CurrentStep.Should().Be(WizardStep.Welcome);
    }

    [Fact]
    public void NextStep_FromWelcome_GoesToSelectDevice()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep();
        vm.CurrentStep.Should().Be(WizardStep.SelectDevice);
    }

    [Fact]
    public void NextStep_FromSelectDevice_GoesToNearCalibration()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep();
        vm.NextStep();
        vm.CurrentStep.Should().Be(WizardStep.NearCalibration);
    }

    [Fact]
    public void NextStep_FromNearCalibration_GoesToAwayCalibration()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); vm.NextStep();
        vm.CurrentStep.Should().Be(WizardStep.AwayCalibration);
    }

    [Fact]
    public void NextStep_FromAwayCalibration_GoesToResults()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); vm.NextStep(); vm.NextStep();
        vm.CurrentStep.Should().Be(WizardStep.Results);
    }

    [Fact]
    public void PreviousStep_FromSelectDevice_GoesToWelcome()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep();
        vm.PreviousStep();
        vm.CurrentStep.Should().Be(WizardStep.Welcome);
    }

    [Fact]
    public void PreviousStep_FromResults_GoesToAwayCalibration()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); vm.NextStep(); vm.NextStep();
        vm.PreviousStep();
        vm.CurrentStep.Should().Be(WizardStep.AwayCalibration);
    }

    [Fact]
    public void StartCollecting_SetsIsCollectingSamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); // Go to NearCalibration
        vm.StartCollecting();
        vm.IsCollectingSamples.Should().BeTrue();
    }

    [Fact]
    public void StopCollecting_ClearsIsCollectingSamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep();
        vm.StartCollecting();
        vm.StopCollecting();
        vm.IsCollectingSamples.Should().BeFalse();
    }

    [Fact]
    public void OnRssiReading_WhenCollecting_AddsToNearSamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); // Go to NearCalibration
        vm.StartCollecting();

        vm.OnRssiReading(-65.0);
        vm.OnRssiReading(-67.0);

        vm.NearSamples.Should().HaveCount(2);
    }

    [Fact]
    public void OnRssiReading_WhenNotCollecting_DoesNotAddSample()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); // Go to NearCalibration

        vm.OnRssiReading(-65.0);

        vm.NearSamples.Should().BeEmpty();
    }

    [Fact]
    public void OnRssiReading_UpdatesCurrentRssi()
    {
        var vm = new CalibrationWizardViewModel();
        vm.OnRssiReading(-70.0);
        vm.CurrentRssi.Should().Be(-70.0);
    }

    [Fact]
    public void OnRssiReading_AwayStep_AddsToAwaySamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); vm.NextStep(); // Go to AwayCalibration
        vm.StartCollecting();

        vm.OnRssiReading(-85.0);

        vm.AwaySamples.Should().HaveCount(1);
    }

    [Fact]
    public void StartCollecting_ClearsPreviousSamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); // NearCalibration
        vm.StartCollecting();
        vm.OnRssiReading(-65.0);
        vm.StopCollecting();

        // Start again
        vm.StartCollecting();
        vm.NearSamples.Should().BeEmpty();
    }

    [Fact]
    public void StopCollecting_WithBothSamples_CalculatesRecommendations()
    {
        var vm = new CalibrationWizardViewModel();

        // Collect near samples
        vm.NextStep(); vm.NextStep(); // NearCalibration
        vm.StartCollecting();
        for (var i = 0; i < 5; i++) vm.OnRssiReading(-65.0);
        vm.StopCollecting();

        // Collect away samples
        vm.NextStep(); // AwayCalibration
        vm.StartCollecting();
        for (var i = 0; i < 5; i++) vm.OnRssiReading(-85.0);
        vm.StopCollecting();

        vm.RecommendedUnlockThreshold.Should().NotBe(0);
        vm.RecommendedLockThreshold.Should().NotBe(0);
    }

    [Fact]
    public void Cancel_FiresWizardClosedEvent()
    {
        var vm = new CalibrationWizardViewModel();
        var closed = false;
        vm.WizardClosed += (_, _) => closed = true;

        vm.Cancel();

        closed.Should().BeTrue();
    }

    [Fact]
    public void ApplyThresholds_FiresThresholdsAppliedEvent()
    {
        var vm = new CalibrationWizardViewModel();
        vm.RecommendedLockThreshold = -80;
        vm.RecommendedUnlockThreshold = -65;

        ThresholdRecommendation? recommendation = null;
        vm.ThresholdsApplied += (_, r) => recommendation = r;

        vm.ApplyThresholds();

        recommendation.Should().NotBeNull();
        recommendation!.LockThreshold.Should().Be(-80);
        recommendation.UnlockThreshold.Should().Be(-65);
    }

    [Fact]
    public void ApplyThresholds_AlsoFiresWizardClosedEvent()
    {
        var vm = new CalibrationWizardViewModel();
        var closed = false;
        vm.WizardClosed += (_, _) => closed = true;

        vm.ApplyThresholds();

        closed.Should().BeTrue();
    }

    [Fact]
    public void InstructionText_ChangesWithStep()
    {
        var vm = new CalibrationWizardViewModel();
        var welcomeText = vm.InstructionText;

        vm.NextStep();
        var selectText = vm.InstructionText;

        welcomeText.Should().NotBe(selectText);
    }

    [Fact]
    public void SampleCount_TracksCollectedSamples()
    {
        var vm = new CalibrationWizardViewModel();
        vm.NextStep(); vm.NextStep(); // NearCalibration
        vm.StartCollecting();

        vm.OnRssiReading(-65.0);
        vm.OnRssiReading(-66.0);
        vm.OnRssiReading(-67.0);

        vm.SampleCount.Should().Be(3);
    }
}
