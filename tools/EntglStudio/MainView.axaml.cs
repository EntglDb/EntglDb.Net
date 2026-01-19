using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EntglDb.Core;
using EntglDb.Core.Storage;
using EntglDb.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lifter.Avalonia;
using EntglDb.Core.Network;

namespace EntglStudio;

public partial class MainView : UserControl, IHostedView
{
    private IPeerDatabase? _db;
    private IPeerStore? _store;
    private string? _selectedCollection;

    public ObservableCollection<string> Collections { get; } = new();
    public ObservableCollection<DocumentViewModel> Documents { get; } = new();

    public MainView()
    {
        InitializeComponent();
        
        // Bind Lists
        LstCollections.ItemsSource = Collections;
        GridData.ItemsSource = Documents;
    }

    private async void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        var path = TxtDbPath.Text;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            LblStatus.Text = "Connecting...";
            
            // 1. Create Store
            var connStr = $"Data Source={path}";
            _store = new SqlitePeerStore(connStr, NullLogger<SqlitePeerStore>.Instance);
            var config = new StaticPeerNodeConfigurationProvider(new PeerNodeConfiguration
            {
                NodeId = Guid.NewGuid().ToString()
            });

            // 2. Create Database
            _db = new PeerDatabase(_store, config);
            await _db.InitializeAsync();

            LblStatus.Text = "Connected";
            
            // 3. Load Collections
            await RefreshCollections();
        }
        catch (Exception ex)
        {
            LblStatus.Text = $"Error: {ex.Message}";
        }
    }

    private async void OnRefreshCollectionsClicked(object? sender, RoutedEventArgs e)
    {
        await RefreshCollections();
    }

    private async Task RefreshCollections()
    {
        if (_db == null) return;

        Collections.Clear();
        var cols = await _db.GetCollectionsAsync();
        foreach (var c in cols)
        {
            Collections.Add(c);
        }
    }

    private void OnCollectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var col = LstCollections.SelectedItem as string;
        if (col == _selectedCollection) return;

        _selectedCollection = col;
        LblCurrentCollection.Text = _selectedCollection ?? "No Collection Selected";
        
        if (_selectedCollection != null)
        {
            _ = RefreshData();
        }
    }

    private async void OnRefreshDataClicked(object? sender, RoutedEventArgs e)
    {
        await RefreshData();
    }

    private async Task RefreshData()
    {
        if (_store == null || _selectedCollection == null) return;

        Documents.Clear();

        try
        {
            // Use Store directly to get Document objects (Key + JsonContent)
            // Query with null expression = All
            var docs = await _store.QueryDocumentsAsync(_selectedCollection, null);

            foreach (var d in docs)
            {
                if (!d.IsDeleted) // Skip deleted
                {
                    Documents.Add(new DocumentViewModel
                    {
                        Key = d.Key,
                        Content = d.Content.ValueKind == System.Text.Json.JsonValueKind.Undefined 
                                  ? "{}" 
                                  : d.Content.GetRawText()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Documents.Add(new DocumentViewModel { Key = "Error", Content = ex.Message });
        }
    }

    private async void OnEnsureIndexClicked(object? sender, RoutedEventArgs e)
    {
        if (_store == null || _selectedCollection == null) return;

        // For now, just a demo index on "name" or "Age"
        // In a real tool, we'd ask for the property name.
        // Let's index "Age" just to verify functionality.
        try 
        {
            await _store.EnsureIndexAsync(_selectedCollection, "Age");
            LblStatus.Text = "Index on 'Age' ensured.";
        }
        catch(Exception ex)
        {
            LblStatus.Text = $"Index Error: {ex.Message}";
        }
    }
}

public class DocumentViewModel
{
    public string Key { get; set; } = "";
    public string Content { get; set; } = "";
}
