using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Core.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Management;

/// <summary>
/// Implementation of peer management service.
/// Provides CRUD operations for managing remote peer configurations.
/// </summary>
public class PeerManagementService : IPeerManagementService
{
    private readonly IPeerStore _peerStore;
    private readonly ILogger<PeerManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the PeerManagementService class.
    /// </summary>
    /// <param name="peerStore">Store for persisting peer configurations.</param>
    /// <param name="logger">Logger instance.</param>
    public PeerManagementService(IPeerStore peerStore, ILogger<PeerManagementService>? logger = null)
    {
        _peerStore = peerStore ?? throw new ArgumentNullException(nameof(peerStore));
        _logger = logger ?? NullLogger<PeerManagementService>.Instance;
    }

    public async Task AddCloudPeerAsync(string nodeId, string address, OAuth2Configuration oauth2Config, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);
        ValidateAddress(address);
        ValidateOAuth2Config(oauth2Config);

        var oauth2Json = JsonSerializer.Serialize(oauth2Config);

        var config = new RemotePeerConfiguration
        {
            NodeId = nodeId,
            Address = address,
            Type = PeerType.CloudRemote,
            OAuth2Json = oauth2Json,
            IsEnabled = true
        };

        await _peerStore.SaveRemotePeerAsync(config, cancellationToken);
        _logger.LogInformation("Added cloud remote peer: {NodeId} at {Address}", nodeId, address);
    }

    public async Task AddStaticPeerAsync(string nodeId, string address, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);
        ValidateAddress(address);

        var config = new RemotePeerConfiguration
        {
            NodeId = nodeId,
            Address = address,
            Type = PeerType.StaticRemote,
            OAuth2Json = null,
            IsEnabled = true
        };

        await _peerStore.SaveRemotePeerAsync(config, cancellationToken);
        _logger.LogInformation("Added static remote peer: {NodeId} at {Address}", nodeId, address);
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        await _peerStore.RemoveRemotePeerAsync(nodeId, cancellationToken);
        _logger.LogInformation("Removed remote peer: {NodeId}", nodeId);
    }

    public async Task<IEnumerable<RemotePeerConfiguration>> GetAllRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        return await _peerStore.GetRemotePeersAsync(cancellationToken);
    }

    public async Task EnablePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        var peers = await _peerStore.GetRemotePeersAsync(cancellationToken);
        var peer = peers.FirstOrDefault(p => p.NodeId == nodeId);

        if (peer == null)
        {
            throw new InvalidOperationException($"Remote peer '{nodeId}' not found");
        }

        if (!peer.IsEnabled)
        {
            peer.IsEnabled = true;
            await _peerStore.SaveRemotePeerAsync(peer, cancellationToken);
            _logger.LogInformation("Enabled remote peer: {NodeId}", nodeId);
        }
    }

    public async Task DisablePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        var peers = await _peerStore.GetRemotePeersAsync(cancellationToken);
        var peer = peers.FirstOrDefault(p => p.NodeId == nodeId);

        if (peer == null)
        {
            throw new InvalidOperationException($"Remote peer '{nodeId}' not found");
        }

        if (peer.IsEnabled)
        {
            peer.IsEnabled = false;
            await _peerStore.SaveRemotePeerAsync(peer, cancellationToken);
            _logger.LogInformation("Disabled remote peer: {NodeId}", nodeId);
        }
    }

    private static void ValidateNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            throw new ArgumentException("NodeId cannot be null or empty", nameof(nodeId));
        }
    }

    private static void ValidateAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("Address cannot be null or empty", nameof(address));
        }

        // Basic format validation (should contain host:port)
        if (!address.Contains(':'))
        {
            throw new ArgumentException("Address must be in format 'host:port'", nameof(address));
        }
    }

    private static void ValidateOAuth2Config(OAuth2Configuration config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.Authority))
        {
            throw new ArgumentException("OAuth2 Authority is required", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.ClientId))
        {
            throw new ArgumentException("OAuth2 ClientId is required", nameof(config));
        }

        if (string.IsNullOrWhiteSpace(config.ClientSecret))
        {
            throw new ArgumentException("OAuth2 ClientSecret is required", nameof(config));
        }
    }
}
