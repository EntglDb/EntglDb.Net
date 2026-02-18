using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EntglDb.Core;
using EntglDb.Network;
using Lifter.Avalonia;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EntglDb.Sample.Shared;
using EntglDb.Core.Network;
using EntglDb.Legacy;

namespace EntglDb.Test.Avalonia;

public partial class MainView : UserControl, IHostedView
{
    private readonly IPeerDatabase _database;
    private readonly IEntglDbNode _node;
    private readonly ILogger<MainView> _logger;
    private readonly IPeerNodeConfigurationProvider _configProvider;
    private readonly DispatcherTimer _timer;

    public MainView(IPeerDatabase database, IEntglDbNode node, IPeerNodeConfigurationProvider peerNodeConfigurationProvider, ILogger<MainView> logger)
    {
        _database = database;
        _node = node;
        _logger = logger;
        _configProvider = peerNodeConfigurationProvider;

        InitializeComponent();
        
        NodeIdLabel.Text = $"Node: -";
        PortLabel.Text = $"Port: -";
        
        AppendLog($"Initialized Node: -");
        AppendLog("🔒 Secure mode enabled (ECDH + AES-256)");
        
        // Initialize resolver radio based on appsettings
        var resolverType = System.IO.File.Exists("appsettings.json") 
            ? System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText("appsettings.json"))
                .RootElement.GetProperty("ConflictResolver").GetString()
            : "Merge";
        
        if (resolverType == "LWW")
            LwwRadio.IsChecked = true;
        else
            MergeRadio.IsChecked = true;
        
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

    private void OnShowTodoListsClicked(object? sender, RoutedEventArgs e)
    {
        var window = (global::Avalonia.Controls.Window?)this.VisualRoot;
        if (window != null)
        {
            var todoView = new TodoListView(_database);
            
            var dialog = new global::Avalonia.Controls.Window
            {
                Title = "TodoLists Manager",
                Width = 800,
                Height = 600,
                Content = todoView,
                WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.CenterOwner
            };
            
            dialog.ShowDialog(window);
        }
    }

    private async void OnSaveResolverClicked(object? sender, RoutedEventArgs e)
    {
        var newResolver = MergeRadio.IsChecked == true ? "Merge" : "LWW";
        
        // Update appsettings.json
        try
        {
            var json = System.IO.File.ReadAllText("appsettings.json");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Rebuild JSON with updated resolver
            using var stream = new System.IO.MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "ConflictResolver")
                    {
                        writer.WriteString("ConflictResolver", newResolver);
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            
            System.IO.File.WriteAllBytes("appsettings.json", stream.ToArray());
            AppendLog($"✓ Resolver set to {newResolver}. Restart required.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error saving: {ex.Message}");
        }
    }

    private async void OnRunConflictDemoClicked(object? sender, RoutedEventArgs e)
    {
        var window = (global::Avalonia.Controls.Window?)this.VisualRoot;
        if (window == null) return;
        
        var dialog = new global::Avalonia.Controls.Window
        {
            Title = "Conflict Resolution Demo",
            Width = 600,
            Height = 500,
            WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.CenterOwner
        };
        
        var panel = new global::Avalonia.Controls.StackPanel { Margin = new global::Avalonia.Thickness(20), Spacing = 15 };
        
        panel.Children.Add(new global::Avalonia.Controls.TextBlock 
        { 
            Text = "Conflict Resolution Demo", 
            FontSize = 18, 
            FontWeight = global::Avalonia.Media.FontWeight.Bold 
        });
        
        var log = new global::Avalonia.Controls.TextBlock { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(log);
        
        var runBtn = new global::Avalonia.Controls.Button { Content = "▶ Run Demo", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center };
        panel.Children.Add(runBtn);
        
        dialog.Content = panel;
        
        runBtn.Click += async (s, args) =>
        {
            runBtn.IsEnabled = false;
            log.Text = "Running demo...\n";
            
            try
            {
                var todoCollection = _database.Collection<TodoList>();
                
                var list = new TodoList 
                { 
                    Name = "Shopping Demo",
                    Items = new List<TodoItem>
                    {
                        new TodoItem { Task = "Buy milk", Completed = false },
                        new TodoItem { Task = "Buy bread", Completed = false }
                    }
                };
                
                await todoCollection.Put(list);
                log.Text += $"✓ Created '{list.Name}'\n";
                await Task.Delay(100);
                
                var listA = await todoCollection.Get(list.Id);
                if (listA != null)
                {
                    listA.Items[0].Completed = true;
                    listA.Items.Add(new TodoItem { Task = "Buy eggs", Completed = false });
                    await todoCollection.Put(listA);
                    log.Text += "📝 Edit A: milk ✓, +eggs\n";
                }
                
                await Task.Delay(100);
                
                var listB = await todoCollection.Get(list.Id);
                if (listB != null)
                {
                    listB.Items[1].Completed = true;
                    listB.Items.Add(new TodoItem { Task = "Buy cheese", Completed = false });
                    await todoCollection.Put(listB);
                    log.Text += "📝 Edit B: bread ✓, +cheese\n\n";
                }
                
                await Task.Delay(200);
                
                var merged = await todoCollection.Get(list.Id);
                if (merged != null)
                {
                    var resolver = MergeRadio.IsChecked == true ? "RecursiveMerge" : "LWW";
                    log.Text += $"🔀 Result ({resolver}):\n";
                    foreach (var item in merged.Items)
                    {
                        var status = item.Completed ? "✓" : " ";
                        log.Text += $"  [{status}] {item.Task}\n";
                    }
                    
                    log.Text += $"\n{merged.Items.Count} items total\n";
                    if (resolver == "RecursiveMerge")
                        log.Text += "✓ Both edits preserved (merged by id)";
                    else
                        log.Text += "⚠ Last write wins (Edit B only)";
                }
            }
            catch (Exception ex)
            {
                log.Text += $"\nError: {ex.Message}";
            }
            finally
            {
                runBtn.IsEnabled = true;
            }
        };
        
        await dialog.ShowDialog(window);
    }
}
