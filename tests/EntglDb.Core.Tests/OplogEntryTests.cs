using System;
using System.Text.Json;
using Xunit;
using EntglDb.Core;
using System.Globalization;

namespace EntglDb.Core.Tests
{
    public class OplogEntryTests
    {
        [Fact]
        public void ComputeHash_ShouldBeDeterministic_RegardlessOfPayload()
        {
            // Arrange
            var collection = "test-collection";
            var key = "test-key";
            var op = OperationType.Put;
            var timestamp = new HlcTimestamp(100, 0, "node-1");
            var prevHash = "prev-hash";

            var payload1 = JsonDocument.Parse("{\"prop\": 1}").RootElement;
            var payload2 = JsonDocument.Parse("{\"prop\": 2, \"extra\": \"whitespace\"}").RootElement;

            // Act
            var entry1 = new OplogEntry(collection, key, op, payload1, timestamp, prevHash);
            var entry2 = new OplogEntry(collection, key, op, payload2, timestamp, prevHash);

            // Assert
            Assert.Equal(entry1.Hash, entry2.Hash);
        }

        [Fact]
        public void ComputeHash_ShouldUseInvariantCulture_ForTimestamp()
        {
            // Arrange
            var originalCulture = CultureInfo.CurrentCulture;
            try 
            {
                var culture = CultureInfo.GetCultureInfo("de-DE"); 
                CultureInfo.CurrentCulture = culture;

                var timestamp = new HlcTimestamp(123456789, 1, "node");
                var entry = new OplogEntry("col", "key", OperationType.Put, null, timestamp, "prev");

                // Act
                var hash = entry.ComputeHash();

                // Assert
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var expectedEntry = new OplogEntry("col", "key", OperationType.Put, null, timestamp, "prev");
                Assert.Equal(expectedEntry.Hash, hash);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }
        
        [Fact]
        public void IsValid_ShouldReturnTrue_WhenHashMatches()
        {
             var timestamp = new HlcTimestamp(100, 0, "node-1");
             var entry = new OplogEntry("col", "key", OperationType.Put, null, timestamp, "prev");
             
             Assert.True(entry.IsValid());
        }

        // ── Hash format ──────────────────────────────────────────────────────

        [Fact]
        public void ComputeHash_ShouldProduceLowercaseHex_Of64Chars()
        {
            var entry = new OplogEntry("col", "key", OperationType.Put, null,
                new HlcTimestamp(1, 0, "n"), "prev");

            Assert.Equal(64, entry.Hash.Length);
            Assert.Equal(entry.Hash, entry.Hash.ToLowerInvariant()); // no uppercase
            Assert.Matches("^[0-9a-f]+$", entry.Hash);
        }

        // ── Hash sensitivity to each input field ─────────────────────────────

        [Fact]
        public void ComputeHash_ChangeCollection_ProducesDifferentHash()
        {
            var ts = new HlcTimestamp(1, 0, "n");
            var base_  = new OplogEntry("colA", "key", OperationType.Put, null, ts, "p");
            var other  = new OplogEntry("colB", "key", OperationType.Put, null, ts, "p");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_ChangeKey_ProducesDifferentHash()
        {
            var ts = new HlcTimestamp(1, 0, "n");
            var base_  = new OplogEntry("col", "key1", OperationType.Put, null, ts, "p");
            var other  = new OplogEntry("col", "key2", OperationType.Put, null, ts, "p");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_ChangeOperation_ProducesDifferentHash()
        {
            var ts = new HlcTimestamp(1, 0, "n");
            var put    = new OplogEntry("col", "key", OperationType.Put,    null, ts, "p");
            var delete = new OplogEntry("col", "key", OperationType.Delete, null, ts, "p");

            Assert.NotEqual(put.Hash, delete.Hash);
        }

        [Fact]
        public void ComputeHash_ChangePhysicalTime_ProducesDifferentHash()
        {
            var ts1 = new HlcTimestamp(100, 0, "n");
            var ts2 = new HlcTimestamp(200, 0, "n");
            var base_  = new OplogEntry("col", "key", OperationType.Put, null, ts1, "p");
            var other  = new OplogEntry("col", "key", OperationType.Put, null, ts2, "p");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_ChangeLogicalCounter_ProducesDifferentHash()
        {
            var ts1 = new HlcTimestamp(1, 0, "n");
            var ts2 = new HlcTimestamp(1, 1, "n");
            var base_  = new OplogEntry("col", "key", OperationType.Put, null, ts1, "p");
            var other  = new OplogEntry("col", "key", OperationType.Put, null, ts2, "p");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_ChangeNodeId_ProducesDifferentHash()
        {
            var ts1 = new HlcTimestamp(1, 0, "node-A");
            var ts2 = new HlcTimestamp(1, 0, "node-B");
            var base_  = new OplogEntry("col", "key", OperationType.Put, null, ts1, "p");
            var other  = new OplogEntry("col", "key", OperationType.Put, null, ts2, "p");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_ChangePreviousHash_ProducesDifferentHash()
        {
            var ts = new HlcTimestamp(1, 0, "n");
            var base_  = new OplogEntry("col", "key", OperationType.Put, null, ts, "prev-A");
            var other  = new OplogEntry("col", "key", OperationType.Put, null, ts, "prev-B");

            Assert.NotEqual(base_.Hash, other.Hash);
        }

        [Fact]
        public void ComputeHash_PayloadDoesNotAffectHash()
        {
            // Payload is deliberately excluded from the hash (chain integrity is
            // content-agnostic — the hash covers identity fields only).
            var ts = new HlcTimestamp(1, 0, "n");
            var payloadA = System.Text.Json.JsonDocument.Parse("""{"x":1}""").RootElement;
            var payloadB = System.Text.Json.JsonDocument.Parse("""{"x":999}""").RootElement;

            var entryA = new OplogEntry("col", "key", OperationType.Put, payloadA, ts, "p");
            var entryB = new OplogEntry("col", "key", OperationType.Put, payloadB, ts, "p");

            Assert.Equal(entryA.Hash, entryB.Hash);
        }

        // ── IsValid ───────────────────────────────────────────────────────────

        [Fact]
        public void IsValid_ShouldReturnFalse_WhenHashTampered()
        {
            var ts = new HlcTimestamp(1, 0, "n");
            // Provide an explicit wrong hash via the optional parameter
            var tampered = new OplogEntry("col", "key", OperationType.Put, null, ts, "p",
                hash: "0000000000000000000000000000000000000000000000000000000000000000");

            Assert.False(tampered.IsValid());
        }

        [Fact]
        public void IsValid_ShouldReturnFalse_WhenCollectionChangesAfterCreation()
        {
            // Simulate chain tampering: create entry, then compute hash with different collection.
            var ts = new HlcTimestamp(1, 0, "n");
            var original = new OplogEntry("col", "key", OperationType.Put, null, ts, "p");
            // Use the original's hash but with a different collection — IsValid computes fresh hash.
            var tampered = new OplogEntry("OTHER-COL", "key", OperationType.Put, null, ts, "p",
                hash: original.Hash);

            Assert.False(tampered.IsValid());
        }
    }
}
