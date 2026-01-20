using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EntglDb.Core.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EntglDb.Core.Management;

/// <summary>
/// Implementation of peer management service.
/// Provides CRUD operations for managing remote peer configurations.
/// 
/// Remote peer configurations are stored in a synchronized collection and automatically
/// replicated across all nodes in the cluster. Any change made on one node will be
/// synchronized to all other nodes through the normal EntglDB sync process.
/// </summary>
public class PeerManagementService : IPeerManagementService
{
    private readonly IPeerDatabase _database;
    private readonly ILogger<PeerManagementService> _logger;
    private const string RemotePeersCollectionName = "_system_remote_peers";

    /// <summary>
    /// Initializes a new instance of the PeerManagementService class.
    /// </summary>
    /// <param name="database">Database instance for accessing the synchronized collection.</param>
    /// <param name="logger">Logger instance.</param>
    public PeerManagementService(IPeerDatabase database, ILogger<PeerManagementService>? logger = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? NullLogger<PeerManagementService>.Instance;
    }

    private IPeerCollection<RemotePeerConfiguration> GetRemotePeersCollection()
    {
        return _database.Collection<RemotePeerConfiguration>(RemotePeersCollectionName);
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

        var collection = GetRemotePeersCollection();
        await collection.Put(nodeId, config, cancellationToken);
        _logger.LogInformation("Added cloud remote peer: {NodeId} at {Address} (will sync to all cluster nodes)", nodeId, address);
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

        var collection = GetRemotePeersCollection();
        await collection.Put(nodeId, config, cancellationToken);
        _logger.LogInformation("Added static remote peer: {NodeId} at {Address} (will sync to all cluster nodes)", nodeId, address);
    }

    public async Task RemoveRemotePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        var collection = GetRemotePeersCollection();
        await collection.Delete(nodeId, cancellationToken);
        _logger.LogInformation("Removed remote peer: {NodeId} (will sync to all cluster nodes)", nodeId);
    }

    public async Task<IEnumerable<RemotePeerConfiguration>> GetAllRemotePeersAsync(CancellationToken cancellationToken = default)
    {
        var collection = GetRemotePeersCollection();
        return await collection.Find(p => true, cancellationToken);
    }

    public async Task EnablePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        var collection = GetRemotePeersCollection();
        var peer = await collection.Get(nodeId, cancellationToken);

        if (peer == null)
        {
            throw new InvalidOperationException($"Remote peer '{nodeId}' not found");
        }

        if (!peer.IsEnabled)
        {
            peer.IsEnabled = true;
            await collection.Put(nodeId, peer, cancellationToken);
            _logger.LogInformation("Enabled remote peer: {NodeId} (will sync to all cluster nodes)", nodeId);
        }
    }

    public async Task DisablePeerAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        ValidateNodeId(nodeId);

        var collection = GetRemotePeersCollection();
        var peer = await collection.Get(nodeId, cancellationToken);

        if (peer == null)
        {
            throw new InvalidOperationException($"Remote peer '{nodeId}' not found");
        }

        if (peer.IsEnabled)
        {
            peer.IsEnabled = false;
            await collection.Put(nodeId, peer, cancellationToken);
            _logger.LogInformation("Disabled remote peer: {NodeId} (will sync to all cluster nodes)", nodeId);
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
