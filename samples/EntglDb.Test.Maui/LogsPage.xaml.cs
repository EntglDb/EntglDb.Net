using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace EntglDb.Test.Maui;

public partial class LogsPage : ContentPage
{
    private readonly ConcurrentQueue<LogEntry> _logsQueue;
    public ObservableCollection<LogEntry> Logs { get; } = new();

    public LogsPage(ConcurrentQueue<LogEntry> logsQueue)
    {
        InitializeComponent();
        _logsQueue = logsQueue;
        BindingContext = this;
        
        // Auto-refresh timer
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!this.IsLoaded) return false;
            RefreshLogs();
            return true;
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshLogs();
    }

    private void RefreshLogs()
    {
        // Simply copy all new items?
        // For simplicity, we can just rebuild the ObservableCollection from the Queue if it changed significantly,
        // or smarter, track what we added.
        // Given concurrent queue limits to 1000, let's just clear and re-add or sync.
        // Actually, syncing 1000 items every second in UI might be heavy.
        
        // Let's just grab the snapshot.
        var snapshot = _logsQueue.ToArray();
        
        if (snapshot.Length != Logs.Count || (snapshot.Length > 0 && Logs.Count > 0 && snapshot.Last() != Logs.Last()))
        {
            Logs.Clear();
            foreach (var log in snapshot)
            {
                Logs.Add(log);
            }
            // Scroll to bottom
            if (Logs.Count > 0)
                LogsCollectionView.ScrollTo(Logs.Last());
        }
    }

    private void OnClearClicked(object sender, EventArgs e)
    {
        while (_logsQueue.TryDequeue(out _)) { }
        Logs.Clear();
    }
}
