using EntglDb.Core;
using FluentAssertions;
using Xunit;

namespace EntglDb.Persistence.Sqlite.Tests;

public class HlcTimestampTests
{
    [Fact]
    public void Constructor_ShouldCreateTimestamp()
    {
        // Act
        var timestamp = new HlcTimestamp(1000, 5, "node1");

        // Assert
        timestamp.PhysicalTime.Should().Be(1000);
        timestamp.LogicalCounter.Should().Be(5);
        timestamp.NodeId.Should().Be("node1");
    }

    [Fact]
    public void CompareTo_ShouldCompareByPhysicalTime_First()
    {
        // Arrange
        var ts1 = new HlcTimestamp(1000, 0, "node1");
        var ts2 = new HlcTimestamp(2000, 0, "node2");

        // Act & Assert
        ts1.CompareTo(ts2).Should().BeLessThan(0);
        ts2.CompareTo(ts1).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_ShouldCompareByLogicalCounter_WhenPhysicalTimeEqual()
    {
        // Arrange
        var ts1 = new HlcTimestamp(1000, 5, "node1");
        var ts2 = new HlcTimestamp(1000, 10, "node2");

        // Act & Assert
        ts1.CompareTo(ts2).Should().BeLessThan(0);
        ts2.CompareTo(ts1).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_ShouldCompareByNodeId_WhenBothEqual()
    {
        // Arrange
        var ts1 = new HlcTimestamp(1000, 5, "node1");
        var ts2 = new HlcTimestamp(1000, 5, "node2");

        // Act & Assert
        ts1.CompareTo(ts2).Should().BeLessThan(0);
        ts2.CompareTo(ts1).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_ShouldReturnZero_WhenEqual()
    {
        // Arrange
        var ts1 = new HlcTimestamp(1000, 5, "node1");
        var ts2 = new HlcTimestamp(1000, 5, "node1");

        // Act & Assert
        ts1.CompareTo(ts2).Should().Be(0);
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var timestamp = new HlcTimestamp(1234567890, 42, "test-node");

        // Act
        var str = timestamp.ToString();

        // Assert
        str.Should().Contain("1234567890");
        str.Should().Contain("42");
        str.Should().Contain("test-node");
    }
}
