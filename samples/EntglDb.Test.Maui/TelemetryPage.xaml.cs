using EntglDb.Network.Telemetry;
using System.Timers;

namespace EntglDb.Test.Maui;

public partial class TelemetryPage : ContentPage
{
    private readonly INetworkTelemetryService _telemetry;
    private readonly System.Timers.Timer _timer;

    public TelemetryPage(INetworkTelemetryService telemetry)
    {
        InitializeComponent();
        _telemetry = telemetry;
        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += Timer_Elapsed;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshMetrics();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(RefreshMetrics);
    }

    private void Button_Clicked(object sender, EventArgs e)
    {
        RefreshMetrics();
    }

    private void RefreshMetrics()
    {
        var snapshot = _telemetry.GetSnapshot();

        // Compression: Show % Saved
        UpdateLabels(MetricType.CompressionRatio, LblComp1m, LblComp5m, LblComp10m, LblComp30m, snapshot, 
            val => val > 0 ? $"{(1 - val) * 100:F1}%" : "-");

        // Time Metrics: Show 'ms'
        UpdateLabels(MetricType.EncryptionTime, LblEnc1m, LblEnc5m, LblEnc10m, LblEnc30m, snapshot, 
            val => $"{val:F2} ms");
        UpdateLabels(MetricType.DecryptionTime, LblDec1m, LblDec5m, LblDec10m, LblDec30m, snapshot, 
            val => $"{val:F2} ms");
        UpdateLabels(MetricType.RoundTripTime, LblRtt1m, LblRtt5m, LblRtt10m, LblRtt30m, snapshot, 
            val => $"{val:F0} ms");
    }

    private void UpdateLabels(MetricType type, Label l1, Label l5, Label l10, Label l30, Dictionary<MetricType, Dictionary<int, double>> snapshot, Func<double, string> formatter)
    {
        if (snapshot.TryGetValue(type, out var windows))
        {
            l1.Text = formatter(windows.GetValueOrDefault(60));
            l5.Text = formatter(windows.GetValueOrDefault(300));
            l10.Text = formatter(windows.GetValueOrDefault(600));
            l30.Text = formatter(windows.GetValueOrDefault(1800));
        }
    }
}
