using System;
using EntglDb.Core;
using Xunit;

namespace EntglDb.Core.Tests;

/// <summary>
/// Tests for HlcTimestamp.Parse, TryFormat, and IFormattable — strict on every edge case
/// that could silently corrupt distributed clock comparisons.
/// </summary>
public class HlcTimestampTests
{
    // ── Parse(string) ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidString_ReturnsCorrectComponents()
    {
        var ts = HlcTimestamp.Parse("123456:42:my-node");

        Assert.Equal(123456L, ts.PhysicalTime);
        Assert.Equal(42, ts.LogicalCounter);
        Assert.Equal("my-node", ts.NodeId);
    }

    [Fact]
    public void Parse_ZeroValues_Accepted()
    {
        var ts = HlcTimestamp.Parse("0:0:n");

        Assert.Equal(0L, ts.PhysicalTime);
        Assert.Equal(0, ts.LogicalCounter);
        Assert.Equal("n", ts.NodeId);
    }

    [Fact]
    public void Parse_MaxLong_DoesNotOverflow()
    {
        var ts = HlcTimestamp.Parse($"{long.MaxValue}:0:n");

        Assert.Equal(long.MaxValue, ts.PhysicalTime);
    }

    [Fact]
    public void Parse_MaxInt_DoesNotOverflow()
    {
        var ts = HlcTimestamp.Parse($"0:{int.MaxValue}:n");

        Assert.Equal(int.MaxValue, ts.LogicalCounter);
    }

    [Fact]
    public void Parse_EmptyNodeId_RoundTrips()
    {
        var original = new HlcTimestamp(100, 1, "");
        var parsed = HlcTimestamp.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Parse_NodeIdContainingColons_RoundTrips()
    {
        // Everything after the 2nd ':' is treated as NodeId — including embedded colons.
        var original = new HlcTimestamp(1, 0, "ns:cluster:1");
        var parsed = HlcTimestamp.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Parse_RoundTrip_EqualToOriginal()
    {
        var original = new HlcTimestamp(long.MaxValue, int.MaxValue, "node-ABC_123.region");
        var parsed = HlcTimestamp.Parse(original.ToString());

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("no-colons")]
    [InlineData("100")]
    [InlineData("100:1")] // only one colon — no NodeId delimited
    public void Parse_MissingColons_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse(input));
    }

    [Theory]
    [InlineData("abc:1:node")]   // PhysicalTime non-numeric
    [InlineData("100:xyz:node")] // LogicalCounter non-numeric
    [InlineData(":1:node")]      // Empty PhysicalTime
    [InlineData("100::node")]    // Empty LogicalCounter
    public void Parse_NonNumericComponents_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse(input));
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HlcTimestamp.Parse(null!));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HlcTimestamp.Parse(""));
    }

    // ── Parse(ReadOnlySpan<char>) ────────────────────────────────────────────

    [Fact]
    public void ParseSpan_ValidInput_MatchesParseString()
    {
        const string s = "9999:88:node-XYZ";
        var fromString = HlcTimestamp.Parse(s);
        var fromSpan = HlcTimestamp.Parse(s.AsSpan());

        Assert.Equal(fromString, fromSpan);
    }

    [Fact]
    public void ParseSpan_EmptySpan_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HlcTimestamp.Parse(ReadOnlySpan<char>.Empty));
    }

    [Fact]
    public void ParseSpan_MissingSecondColon_ThrowsFormatException()
    {
        // "100:nocolon" — first colon found, but rest has no second colon
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse("100:nocolon".AsSpan()));
    }

    [Fact]
    public void ParseSpan_NonNumericPhysicalTime_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse("abc:1:node".AsSpan()));
    }

    [Fact]
    public void ParseSpan_NonNumericLogicalCounter_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => HlcTimestamp.Parse("100:xyz:node".AsSpan()));
    }

    [Fact]
    public void ParseSpan_RoundTrip_EqualToOriginal()
    {
        var original = new HlcTimestamp(543210987654L, 7, "replica-02");
        var parsed = HlcTimestamp.Parse(original.ToString().AsSpan());

        Assert.Equal(original, parsed);
    }

    // ── TryFormat(Span<char>) ────────────────────────────────────────────────

    [Fact]
    public void TryFormat_EmptyDestination_ReturnsFalse()
    {
        var ts = new HlcTimestamp(1, 1, "n");
        bool ok = ts.TryFormat(Span<char>.Empty, out _, default, null);

        Assert.False(ok);
    }

    [Fact]
    public void TryFormat_DestinationTooSmall_ReturnsFalse()
    {
        // "100:1:node" = 10 chars; 5 is not enough
        var ts = new HlcTimestamp(100, 1, "node");
        bool ok = ts.TryFormat(new char[5], out _, default, null);

        Assert.False(ok);
    }

    [Fact]
    public void TryFormat_ExactSizeDestination_ReturnsTrue()
    {
        var ts = new HlcTimestamp(100, 1, "node");
        var expected = "100:1:node";
        char[] buf = new char[expected.Length];

        bool ok = ts.TryFormat(buf, out int written, default, null);

        Assert.True(ok);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, new string(buf, 0, written));
    }

    [Fact]
    public void TryFormat_OversizedDestination_WritesOnlyNecessaryChars()
    {
        var ts = new HlcTimestamp(999, 7, "abc");
        var expected = "999:7:abc";
        char[] buf = new char[200];

        bool ok = ts.TryFormat(buf, out int written, default, null);

        Assert.True(ok);
        Assert.Equal(expected.Length, written);
        Assert.Equal(expected, new string(buf, 0, written));
    }

    [Fact]
    public void TryFormat_OutputMatchesToString()
    {
        var ts = new HlcTimestamp(1234567890L, 99, "peer-01");
        char[] buf = new char[100];

        bool ok = ts.TryFormat(buf, out int written, default, null);

        Assert.True(ok);
        Assert.Equal(ts.ToString(), new string(buf, 0, written));
    }

    [Fact]
    public void TryFormat_ResultIsRoundTrippable()
    {
        var original = new HlcTimestamp(9876543210L, 5, "shard-7");
        char[] buf = new char[100];
        original.TryFormat(buf, out int written, default, null);

        var parsed = HlcTimestamp.Parse(new string(buf, 0, written));

        Assert.Equal(original, parsed);
    }

    // ── IFormattable.ToString(string?, IFormatProvider?) ────────────────────

    [Fact]
    public void IFormattable_ToString_IgnoresFormatStringAndProvider()
    {
        var ts = new HlcTimestamp(500, 3, "n1");
        string plain = ts.ToString();

        // Cast via interface — format specifier and foreign culture must be ignored
        string formatted1 = ((IFormattable)ts).ToString("X2", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        string formatted2 = ((IFormattable)ts).ToString(null, null);

        Assert.Equal(plain, formatted1);
        Assert.Equal(plain, formatted2);
    }

    [Fact]
    public void IFormattable_ToString_ContainsAllComponents()
    {
        var ts = new HlcTimestamp(123, 45, "my-node");
        string result = ((IFormattable)ts).ToString(null, null);

        Assert.Contains("123", result);
        Assert.Contains("45", result);
        Assert.Contains("my-node", result);
    }
}
