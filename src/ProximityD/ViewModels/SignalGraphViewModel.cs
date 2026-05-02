using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
#if WINDOWS
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
#endif

namespace ProximityD.ViewModels;

/// <summary>
/// Represents a single data point in the signal graph.
/// </summary>
public record SignalDataPoint(DateTime Time, double RawRssi, double SmoothedRssi);

/// <summary>
/// ViewModel for the real-time signal strength graph.
/// Maintains a rolling 60-second window of RSSI data.
/// On Windows, exposes an OxyPlot PlotModel for rendering.
/// </summary>
public partial class SignalGraphViewModel : ObservableObject
{
    private const int WindowSeconds = 60;
    private readonly List<SignalDataPoint> _dataPoints = new();
    private readonly object _lock = new();

#if WINDOWS
    private PlotModel? _plotModel;
    private LineSeries? _rawRssiSeries;
    private LineSeries? _smoothedRssiSeries;

    /// <summary>Gets the OxyPlot model for binding in XAML.</summary>
    public PlotModel PlotModel => _plotModel ??= CreatePlotModel();

    private PlotModel CreatePlotModel()
    {
        var model = new PlotModel { Title = "Signal Strength" };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Time",
            StringFormat = "HH:mm:ss"
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "RSSI (dBm)",
            Minimum = -100,
            Maximum = -30
        });

        _rawRssiSeries = new LineSeries
        {
            Title = "Raw RSSI",
            Color = OxyColors.LightBlue,
            StrokeThickness = 1
        };

        _smoothedRssiSeries = new LineSeries
        {
            Title = "Smoothed RSSI",
            Color = OxyColors.DodgerBlue,
            StrokeThickness = 2
        };

        model.Series.Add(_rawRssiSeries);
        model.Series.Add(_smoothedRssiSeries);

        return model;
    }
#endif

    [ObservableProperty]
    private string _selectedDeviceId = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    /// <summary>Gets a read-only snapshot of the current data points (for testing).</summary>
    public IReadOnlyList<SignalDataPoint> DataPoints
    {
        get { lock (_lock) { return _dataPoints.ToList().AsReadOnly(); } }
    }

    /// <summary>
    /// Adds a new data point and trims old data outside the rolling window.
    /// </summary>
    public void AddDataPoint(DateTime time, double rawRssi, double smoothedRssi)
    {
        var point = new SignalDataPoint(time, rawRssi, smoothedRssi);
        var cutoff = time - TimeSpan.FromSeconds(WindowSeconds);

        lock (_lock)
        {
            _dataPoints.Add(point);
            _dataPoints.RemoveAll(p => p.Time < cutoff);
        }

#if WINDOWS
        if (_plotModel != null && _rawRssiSeries != null && _smoothedRssiSeries != null)
        {
            var oxyTime = DateTimeAxis.ToDouble(time);
            _rawRssiSeries.Points.Add(new DataPoint(oxyTime, rawRssi));
            _smoothedRssiSeries.Points.Add(new DataPoint(oxyTime, smoothedRssi));

            // Trim OxyPlot series to match window
            while (_rawRssiSeries.Points.Count > 0 &&
                   DateTimeAxis.ToDateTime(_rawRssiSeries.Points[0].X) < cutoff)
            {
                _rawRssiSeries.Points.RemoveAt(0);
                if (_smoothedRssiSeries.Points.Count > 0)
                    _smoothedRssiSeries.Points.RemoveAt(0);
            }

            _plotModel.InvalidatePlot(true);
        }
#endif
    }

    /// <summary>Clears all recorded data.</summary>
    [RelayCommand]
    public void Clear()
    {
        lock (_lock)
        {
            _dataPoints.Clear();
        }

#if WINDOWS
        _rawRssiSeries?.Points.Clear();
        _smoothedRssiSeries?.Points.Clear();
        _plotModel?.InvalidatePlot(true);
#endif
    }
}
