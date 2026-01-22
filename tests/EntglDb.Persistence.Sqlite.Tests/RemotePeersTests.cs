using EntglDb.Core.Network;
using FluentAssertions;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class RemotePeersTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqlitePeerStore _store;

    public RemotePeersTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"remote-peers-test-{Guid.NewGuid()}.db");
        // Using legacy constructor which triggers Initialize()
        _store = new SqlitePeerStore($"Data Source={_dbPath}");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public async Task SaveRemotePeerAsync_ShouldPersistPeer_AndCreateRemotePeersTable()
    {
        // Arrange
        var peer = new RemotePeerConfiguration 
        { 
            NodeId = "remote-node-1", 
            Address = "127.0.0.1:5000", 
            Type = PeerType.StaticRemote, 
            IsEnabled = true 
        };

        // Act
        // This will fail if RemotePeers table does not exist
        await _store.SaveRemotePeerAsync(peer);
        
        var peers = await _store.GetRemotePeersAsync();

        // Assert
        peers.Should().NotBeNull();
        peers.Should().ContainSingle();
        
        var savedPeer = peers.First();
        savedPeer.NodeId.Should().Be("remote-node-1");
        savedPeer.Address.Should().Be("127.0.0.1:5000");
        savedPeer.Type.Should().Be(PeerType.StaticRemote);
        savedPeer.IsEnabled.Should().BeTrue();
    }
}
