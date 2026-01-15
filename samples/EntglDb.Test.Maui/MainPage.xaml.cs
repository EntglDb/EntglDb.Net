using EntglDb.Core;
using EntglDb.Network;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using EntglDb.Sample.Shared;

namespace EntglDb.Test.Maui;

public partial class MainPage : ContentPage
{
	private readonly PeerDatabase _database;
    private readonly EntglDbNode _node;
    private readonly ILogger<MainPage> _logger;
    private readonly IDispatcherTimer _timer;

    public ObservableCollection<string> Peers { get; } = new ObservableCollection<string>();

	public MainPage(PeerDatabase database, EntglDbNode node, ILogger<MainPage> logger)
	{
		InitializeComponent();
		_database = database;
        _node = node;
        _logger = logger;
        
        NodeIdLabel.Text = $"Node: {_database.NodeId}";
        PortLabel.Text = $"Port: {_node.Address.Port}";
        PeersList.ItemsSource = Peers;

        AppendLog($"Initialized Node: {_database.NodeId}");

        // Timer for refreshing peers
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += (s, e) => UpdatePeers();
        _timer.Start();
	}

    private void UpdatePeers()
    {
        var peers = _node.Discovery.GetActivePeers();
        // Update on UI thread
        MainThread.BeginInvokeOnMainThread(() => 
        {
            Peers.Clear();
            foreach(var p in peers)
            {
                Peers.Add($"{p.NodeId} ({p.Address})");
            }
        });
    }

    private void AppendLog(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => 
        {
            var msg = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (string.IsNullOrEmpty(ResultLog.Text))
                ResultLog.Text = msg;
            else
                ResultLog.Text = msg + Environment.NewLine + ResultLog.Text;
            
            StatusLabel.Text = message;
        });
    }

	private async void OnSaveClicked(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(KeyEntry.Text) || string.IsNullOrWhiteSpace(ValueEntry.Text))
		{
			StatusLabel.Text = "Please enter both key and value";
			return;
		}

		try
		{
            var collection = _database.Collection<User>("users");
			var user = new User
            { 
                Id = KeyEntry.Text,
                Name = ValueEntry.Text, 
                Age = new Random().Next(18, 99),
                Address = new Address { City = "MAUI City" }
            };
			
			await collection.Put(user);
			AppendLog($"Saved '{user.Id}' to 'users'");
			
			KeyEntry.Text = string.Empty;
			ValueEntry.Text = string.Empty;
		}
		catch (Exception ex)
		{
			AppendLog($"Error saving: {ex.Message}");
		}
	}

	private async void OnLoadClicked(object? sender, EventArgs e)
	{
		if (string.IsNullOrWhiteSpace(KeyEntry.Text))
		{
			StatusLabel.Text = "Please enter a key, waiting...";
			return;
		}

		try
		{
            var collection = _database.Collection<User>("users");
			var user = await collection.Get(KeyEntry.Text);
			
			if (user != null)
			{
				AppendLog($"Found: {user.Name} ({user.Age})");
			}
			else
			{
				AppendLog($"Key '{KeyEntry.Text}' not found.");
			}
		}
		catch (Exception ex)
		{
			AppendLog($"Error loading: {ex.Message}");
		}
	}

    private void OnAutoDataClicked(object sender, EventArgs e)
    {
        var key = Guid.NewGuid().ToString().Substring(0, 8);
        var val = $"AutoUser-MAUI-{DateTime.Now.Ticks % 10000}";
        KeyEntry.Text = key;
        ValueEntry.Text = val;
        OnSaveClicked(sender, e);
    }

    private async void OnSpamClicked(object sender, EventArgs e)
    {
        AppendLog("Starting Spam (5)...");
        var collection = _database.Collection<User>("users");
        for (int i = 0; i < 5; i++)
        {
            var key = $"Spam-MAUI-{i}-{DateTime.Now.Ticks}";
            var user = new User
            { 
                Id = key,
                Name = $"SpamUser {i}",
                Age = 20 + i,
                Address = new Address { City = "SpamTown" }
            };

            await collection.Put(user);
            AppendLog($"Spammed: {key}");
            await Task.Delay(100);
        }
        AppendLog("Spam finished.");
    }

    private async void OnCountClicked(object sender, EventArgs e)
    {
        var collection = _database.Collection<User>("users");
        var all = await collection.Find(x => true);
        AppendLog($"Total Users: {all.Count()}");
    }

    private void OnClearLogsClicked(object sender, EventArgs e)
    {
        ResultLog.Text = string.Empty;
    }
}
