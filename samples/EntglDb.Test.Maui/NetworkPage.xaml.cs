using EntglDb.Core.Network;
using EntglDb.Network;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace EntglDb.Test.Maui;

public partial class NetworkPage : ContentPage
{
    private readonly IEntglDbNode _node;
    private readonly IPeerNodeConfigurationProvider _configProvider;

    public ObservableCollection<PeerInfoViewModel> Peers { get; } = new();

    public ICommand RefreshPeersCommand { get; }

    public NetworkPage(IEntglDbNode node, IPeerNodeConfigurationProvider configProvider)
    {
        InitializeComponent();
        _node = node;
        _configProvider = configProvider;
        RefreshPeersCommand = new Command(RefreshPeers);
        
        BindingContext = this;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadNodeInfo();
        RefreshPeers();
        
        // Auto-refresh every 5 seconds while on this page
        Dispatcher.StartTimer(TimeSpan.FromSeconds(5), () =>
        {
            if (!this.IsLoaded) return false;
            RefreshPeers();
            return true;
        });
    }

    private async Task LoadNodeInfo()
    {
        var config = await _configProvider.GetConfiguration();
        NodeIdLabel.Text = $"ID: {config.NodeId}";
        AddressLabel.Text = $"Address: {_node.Address}";
    }

    private void RefreshPeers()
    {
        var peers = _node.Discovery.GetActivePeers().ToList();
        
        // Simple sync for updating UI
        Peers.Clear();
        foreach (var p in peers)
        {
            Peers.Add(new PeerInfoViewModel 
            { 
                NodeId = p.NodeId, 
                Address = p.Address.ToString(),
                LastSeen = p.LastSeen.LocalDateTime 
            });
        }
        
        PeersRefreshView.IsRefreshing = false;
    }
}

public class PeerInfoViewModel
{
    public string NodeId { get; set; }
    public string Address { get; set; }
    public DateTime LastSeen { get; set; }
}
