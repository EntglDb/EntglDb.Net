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
    }
}
