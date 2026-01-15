using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EntglDb.Core;
using EntglDb.Network;
using Lifter.Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using EntglDb.Sample.Shared;

namespace EntglDb.Test.Avalonia;

public partial class MainView : UserControl, IHostedView
{
    private readonly PeerDatabase _database;
    private readonly EntglDbNode _node;
    private readonly ILogger<MainView> _logger;
    private readonly DispatcherTimer _timer;

    public MainView(PeerDatabase database, EntglDbNode node, ILogger<MainView> logger)
    {
        _database = database;
        _node = node;
        _logger = logger;
        
        InitializeComponent();
        
        NodeIdLabel.Text = $"Node: {_database.NodeId}";
        PortLabel.Text = $"Port: {_node.Address.Port}";
        
        AppendLog($"Initialized Node: {_database.NodeId}");
        
        // Timer for refreshing peers
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (s, e) => UpdatePeers();
        _timer.Start();
    }

    private void UpdatePeers()
    {
        var peers = _node.Discovery.GetActivePeers();
        PeersList.ItemsSource = peers.Select(p => $"{p.NodeId} ({p.Address})").ToList();
    }

    private void AppendLog(string message)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] {message}";
        ResultLog.Text = msg + Environment.NewLine + ResultLog.Text; // Prepend
        StatusLabel.Text = message;
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyEntry.Text) || string.IsNullOrWhiteSpace(ValueEntry.Text))
        {
            StatusLabel.Text = "Please enter both key and value";
            return;
        }

        try
        {
            // Use strongly typed collection
            var collection = _database.Collection<User>("users");
            
            var user = new User
            { 
                Id = KeyEntry.Text,
                Name = ValueEntry.Text, 
                Age = new Random().Next(18, 99),
                Address = new Address { City = "Avalonia City" }
            };
            
            await collection.Put(user);
            AppendLog($"Saved '{user.Id}' to 'users'");
            
            KeyEntry.Text = string.Empty;
            ValueEntry.Text = string.Empty;
        }
        catch (Exception ex)
        {
            AppendLog($"Error saving: {ex.Message}");
            _logger.LogError(ex, "Error saving");
        }
    }

    private async void OnLoadClicked(object? sender, RoutedEventArgs e)
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

    private async void OnAutoDataClicked(object? sender, RoutedEventArgs e)
    {
        var key = Guid.NewGuid().ToString().Substring(0, 8);
        var val = $"AutoUser-{DateTime.Now.Ticks % 10000}";
        KeyEntry.Text = key;
        ValueEntry.Text = val;
        OnSaveClicked(sender, e);
    }

    private async void OnSpamClicked(object? sender, RoutedEventArgs e)
    {
        AppendLog("Starting Spam (5 records)...");
        var collection = _database.Collection<User>("users");
        for (int i = 0; i < 5; i++)
        {
            var key = $"Spam-{i}-{DateTime.Now.Ticks}";
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

    private async void OnCountClicked(object? sender, RoutedEventArgs e)
    {
        var collection = _database.Collection<User>("users");
        var all = await collection.Find(x => true);
        AppendLog($"Total Users: {all.Count()}");
    }

    private void OnClearLogsClicked(object? sender, RoutedEventArgs e)
    {
        ResultLog.Text = string.Empty;
    }
}
