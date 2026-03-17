using System.Linq;
using EntglDb.Core;
using Xunit;

namespace EntglDb.Core.Tests;

/// <summary>
/// Edge-case tests for VectorClock.CompareTo, GetNodesWithUpdates, and GetNodesToPush.
/// Focuses on asymmetric and boundary scenarios not covered by VectorClockTests.cs.
/// </summary>
public class VectorClockEdgeCaseTests
{
    // ── Two empty clocks ──────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_BothEmpty_ReturnsEqual()
    {
        var vc1 = new VectorClock();
        var vc2 = new VectorClock();

        Assert.Equal(CausalityRelation.Equal, vc1.CompareTo(vc2));
    }

    // ── Asymmetric: one clock is empty ────────────────────────────────────────

    [Fact]
    public void CompareTo_EmptyVsNonEmpty_ReturnsStrictlyBehind()
    {
        var empty = new VectorClock();
        var nonEmpty = new VectorClock();
        nonEmpty.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        Assert.Equal(CausalityRelation.StrictlyBehind, empty.CompareTo(nonEmpty));
    }

    [Fact]
    public void CompareTo_NonEmptyVsEmpty_ReturnsStrictlyAhead()
    {
        var nonEmpty = new VectorClock();
        nonEmpty.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        var empty = new VectorClock();

        Assert.Equal(CausalityRelation.StrictlyAhead, nonEmpty.CompareTo(empty));
    }

    // ── Disjoint node sets ────────────────────────────────────────────────────

    [Fact]
    public void CompareTo_DisjointNodeSets_ReturnsConcurrent()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n2", new HlcTimestamp(100, 0, "n2")); // different node, same time

        // vc1 is ahead for n1 (vc2 has default 0), but vc2 is ahead for n2 (vc1 has default 0)
        Assert.Equal(CausalityRelation.Concurrent, vc1.CompareTo(vc2));
    }

    // ── Strict ordering symmetry ──────────────────────────────────────────────

    [Fact]
    public void CompareTo_StrictlyAhead_ImpliesOtherIsStrictlyBehind()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(200, 0, "n1"));
        vc1.SetTimestamp("n2", new HlcTimestamp(100, 0, "n2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        vc2.SetTimestamp("n2", new HlcTimestamp(100, 0, "n2"));

        Assert.Equal(CausalityRelation.StrictlyAhead, vc1.CompareTo(vc2));
        Assert.Equal(CausalityRelation.StrictlyBehind, vc2.CompareTo(vc1));
    }

    [Fact]
    public void CompareTo_Equal_IsSymmetric()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(50, 3, "n1"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(50, 3, "n1"));

        Assert.Equal(CausalityRelation.Equal, vc1.CompareTo(vc2));
        Assert.Equal(CausalityRelation.Equal, vc2.CompareTo(vc1));
    }

    [Fact]
    public void CompareTo_Concurrent_IsSymmetric()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(200, 0, "n1"));
        vc1.SetTimestamp("n2", new HlcTimestamp(50, 0, "n2"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(50, 0, "n1"));
        vc2.SetTimestamp("n2", new HlcTimestamp(200, 0, "n2"));

        Assert.Equal(CausalityRelation.Concurrent, vc1.CompareTo(vc2));
        Assert.Equal(CausalityRelation.Concurrent, vc2.CompareTo(vc1));
    }

    // ── GetNodesToPush — node only in `this` ──────────────────────────────────

    [Fact]
    public void GetNodesToPush_NodeOnlyInThis_IsReturned()
    {
        // `this` knows about n2; `other` does not → other's default(0) < this → should push
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        vc1.SetTimestamp("n2", new HlcTimestamp(50, 0, "n2")); // only in vc1

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1")); // same

        var toPush = vc1.GetNodesToPush(vc2).ToList();

        Assert.Single(toPush);
        Assert.Contains("n2", toPush);
    }

    [Fact]
    public void GetNodesToPush_AllNodesEqual_ReturnsEmpty()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        Assert.Empty(vc1.GetNodesToPush(vc2));
    }

    [Fact]
    public void GetNodesToPush_ThisEmpty_ReturnsEmpty()
    {
        var empty = new VectorClock();
        var other = new VectorClock();
        other.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        Assert.Empty(empty.GetNodesToPush(other));
    }

    // ── GetNodesWithUpdates — node only in `this` must NOT appear ──────────────

    [Fact]
    public void GetNodesWithUpdates_NodeOnlyInThis_IsNotReturned()
    {
        // `this` has n2; `other` does not.
        // Other cannot be "ahead" for n2 (it has no data for n2).
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        vc1.SetTimestamp("n2", new HlcTimestamp(50, 0, "n2")); // only in vc1

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1")); // same

        var toUpdate = vc1.GetNodesWithUpdates(vc2).ToList();

        Assert.Empty(toUpdate); // n2 only in this → other cannot be ahead
    }

    [Fact]
    public void GetNodesWithUpdates_OtherEmpty_ReturnsEmpty()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        var empty = new VectorClock();

        Assert.Empty(vc1.GetNodesWithUpdates(empty));
    }

    // ── LogicalCounter tie-breaking ───────────────────────────────────────────

    [Fact]
    public void CompareTo_SamePhysicalTimeDifferentLogical_UsesLogicalCounter()
    {
        var vc1 = new VectorClock();
        vc1.SetTimestamp("n1", new HlcTimestamp(100, 5, "n1")); // logical 5

        var vc2 = new VectorClock();
        vc2.SetTimestamp("n1", new HlcTimestamp(100, 3, "n1")); // logical 3; same physical

        Assert.Equal(CausalityRelation.StrictlyAhead, vc1.CompareTo(vc2));
        Assert.Equal(CausalityRelation.StrictlyBehind, vc2.CompareTo(vc1));
    }

    // ── Node known only in `other` (new node joining) ─────────────────────────

    [Fact]
    public void CompareTo_OtherHasAdditionalNode_IsStrictlyAhead()
    {
        var existing = new VectorClock();
        existing.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        var withNewNode = new VectorClock();
        withNewNode.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1")); // same
        withNewNode.SetTimestamp("n2", new HlcTimestamp(10, 0, "n2"));  // new node

        // `existing` doesn't know n2 → default(0) < 10 → withNewNode is ahead
        Assert.Equal(CausalityRelation.StrictlyAhead, withNewNode.CompareTo(existing));
        Assert.Equal(CausalityRelation.StrictlyBehind, existing.CompareTo(withNewNode));
    }

    [Fact]
    public void GetNodesWithUpdates_NewNodeInOther_IsReturned()
    {
        var existing = new VectorClock();
        existing.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));

        var withNewNode = new VectorClock();
        withNewNode.SetTimestamp("n1", new HlcTimestamp(100, 0, "n1"));
        withNewNode.SetTimestamp("n2", new HlcTimestamp(10, 0, "n2"));

        var updates = existing.GetNodesWithUpdates(withNewNode).ToList();

        Assert.Single(updates);
        Assert.Contains("n2", updates);
    }
}
