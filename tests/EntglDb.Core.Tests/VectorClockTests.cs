using EntglDb.Core;
using System.Linq;
using Xunit;

namespace EntglDb.Core.Tests;

public class VectorClockTests
{
    [Fact]
    public void EmptyVectorClock_ShouldReturnDefaultTimestamp()
    {
        // Arrange
        var vc = new VectorClock();

        // Act
        var ts = vc.GetTimestamp("node1");

        // Assert
        Assert.Equal(default(HlcTimestamp), ts);
    }

    [Fact]
    public void SetTimestamp_ShouldStoreTimestamp()
    {
        // Arrange
        var vc = new VectorClock();
        var ts = new HlcTimestamp(100, 1, "node1");

        // Act
        vc.SetTimestamp("node1", ts);

        // Assert
        Assert.Equal(ts, vc.GetTimestamp("node1"));
    }

    [Fact]
    public void NodeIds_ShouldReturnAllNodes()
    {
        // Arrange
        var vc = new VectorClock();
        vc.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var nodeIds = vc.NodeIds.ToList();

        // Assert
        Assert.Equal(2, nodeIds.Count);
        Assert.Contains("node1", nodeIds);
        Assert.Contains("node2", nodeIds);
    }

    [Fact]
    public void CompareTo_EqualClocks_ShouldReturnEqual()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc1.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var result = vc1.CompareTo(vc2);

        // Assert
        Assert.Equal(CausalityRelation.Equal, result);
    }

    [Fact]
    public void CompareTo_StrictlyAhead_ShouldReturnStrictlyAhead()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1")); // Ahead
        vc1.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2")); // Same

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var result = vc1.CompareTo(vc2);

        // Assert
        Assert.Equal(CausalityRelation.StrictlyAhead, result);
    }

    [Fact]
    public void CompareTo_StrictlyBehind_ShouldReturnStrictlyBehind()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1")); // Behind
        vc1.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2")); // Same

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var result = vc1.CompareTo(vc2);

        // Assert
        Assert.Equal(CausalityRelation.StrictlyBehind, result);
    }

    [Fact]
    public void CompareTo_Concurrent_ShouldReturnConcurrent()
    {
        // Arrange - Split brain scenario
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1")); // Node1 ahead
        vc1.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2")); // Node2 behind

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1")); // Node1 behind
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2")); // Node2 ahead

        // Act
        var result = vc1.CompareTo(vc2);

        // Assert
        Assert.Equal(CausalityRelation.Concurrent, result);
    }

    [Fact]
    public void GetNodesWithUpdates_ShouldReturnNodesWhereOtherIsAhead()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc1.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1")); // Ahead
        vc2.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2")); // Same

        // Act
        var nodesToPull = vc1.GetNodesWithUpdates(vc2).ToList();

        // Assert
        Assert.Single(nodesToPull);
        Assert.Contains("node1", nodesToPull);
    }

    [Fact]
    public void GetNodesToPush_ShouldReturnNodesWhereThisIsAhead()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1")); // Ahead
        vc1.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2")); // Same

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2"));

        // Act
        var nodesToPush = vc1.GetNodesToPush(vc2).ToList();

        // Assert
        Assert.Single(nodesToPush);
        Assert.Contains("node1", nodesToPush);
    }

    [Fact]
    public void GetNodesWithUpdates_WhenNewNodeAppearsInOther_ShouldReturnIt()
    {
        // Arrange - Simulates a new node joining the cluster
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node3", new HlcTimestamp(50, 1, "node3")); // New node

        // Act
        var nodesToPull = vc1.GetNodesWithUpdates(vc2).ToList();

        // Assert
        Assert.Single(nodesToPull);
        Assert.Contains("node3", nodesToPull);
    }

    [Fact]
    public void Merge_ShouldTakeMaximumForEachNode()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(200, 1, "node1"));
        vc1.SetTimestamp("node2", new HlcTimestamp(100, 2, "node2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));
        vc2.SetTimestamp("node3", new HlcTimestamp(150, 1, "node3"));

        // Act
        vc1.Merge(vc2);

        // Assert
        Assert.Equal(new HlcTimestamp(200, 1, "node1"), vc1.GetTimestamp("node1")); // Kept max
        Assert.Equal(new HlcTimestamp(200, 2, "node2"), vc1.GetTimestamp("node2")); // Merged max
        Assert.Equal(new HlcTimestamp(150, 1, "node3"), vc1.GetTimestamp("node3")); // Added new
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var vc1 = new VectorClock();
        vc1.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));

        // Act
        var vc2 = vc1.Clone();
        vc2.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Assert
        Assert.Single(vc1.NodeIds);
        Assert.Equal(2, vc2.NodeIds.Count());
    }

    [Fact]
    public void ToString_ShouldReturnReadableFormat()
    {
        // Arrange
        var vc = new VectorClock();
        vc.SetTimestamp("node1", new HlcTimestamp(100, 1, "node1"));
        vc.SetTimestamp("node2", new HlcTimestamp(200, 2, "node2"));

        // Act
        var str = vc.ToString();

        // Assert
        Assert.Contains("node1:100:1:node1", str);
        Assert.Contains("node2:200:2:node2", str);
    }

    [Fact]
    public void SplitBrainScenario_ShouldDetectConcurrency()
    {
        // Arrange - Simulating a network partition scenario
        // Partition 1: node1 and node2 are alive
        var vcPartition1 = new VectorClock();
        vcPartition1.SetTimestamp("node1", new HlcTimestamp(300, 5, "node1"));
        vcPartition1.SetTimestamp("node2", new HlcTimestamp(250, 3, "node2"));
        vcPartition1.SetTimestamp("node3", new HlcTimestamp(100, 1, "node3")); // Old data

        // Partition 2: node3 is isolated
        var vcPartition2 = new VectorClock();
        vcPartition2.SetTimestamp("node1", new HlcTimestamp(150, 2, "node1")); // Old data
        vcPartition2.SetTimestamp("node2", new HlcTimestamp(150, 1, "node2")); // Old data
        vcPartition2.SetTimestamp("node3", new HlcTimestamp(400, 8, "node3")); // New data

        // Act
        var relation = vcPartition1.CompareTo(vcPartition2);
        var partition1NeedsToPull = vcPartition1.GetNodesWithUpdates(vcPartition2).ToList();
        var partition1NeedsToPush = vcPartition1.GetNodesToPush(vcPartition2).ToList();

        // Assert
        Assert.Equal(CausalityRelation.Concurrent, relation);
        Assert.Single(partition1NeedsToPull);
        Assert.Contains("node3", partition1NeedsToPull);
        Assert.Equal(2, partition1NeedsToPush.Count);
        Assert.Contains("node1", partition1NeedsToPush);
        Assert.Contains("node2", partition1NeedsToPush);
    }
}
