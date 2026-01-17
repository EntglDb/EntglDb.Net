using System;
using System.Text.Json;
using EntglDb.Core.Sync;
using FluentAssertions;
using Xunit;

namespace EntglDb.Core.Tests.Sync
{
    public class RecursiveNodeMergeConflictResolverTests
    {
        private readonly RecursiveNodeMergeConflictResolver _resolver;

        public RecursiveNodeMergeConflictResolverTests()
        {
            _resolver = new RecursiveNodeMergeConflictResolver();
        }

        private Document CreateDoc(string key, object data, HlcTimestamp ts)
        {
            var json = JsonSerializer.Serialize(data);
            var element = JsonDocument.Parse(json).RootElement;
            return new Document("test", key, element, ts, false);
        }

        private OplogEntry CreateOp(string key, object data, HlcTimestamp ts)
        {
            var json = JsonSerializer.Serialize(data);
            var element = JsonDocument.Parse(json).RootElement;
            return new OplogEntry("test", key, OperationType.Put, element, ts);
        }

        [Fact]
        public void Resolve_ShouldMergeDisjointFields()
        {
            // Arrange
            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");
            
            var doc = CreateDoc("k1", new { name = "Alice" }, ts1);
            var op = CreateOp("k1", new { age = 30 }, ts2);

            // Act
            var result = _resolver.Resolve(doc, op);

            // Assert
            result.ShouldApply.Should().BeTrue();
            result.MergedDocument.Should().NotBeNull();
            
            var merged = result.MergedDocument.Content;
            merged.GetProperty("name").GetString().Should().Be("Alice");
            merged.GetProperty("age").GetInt32().Should().Be(30);
            result.MergedDocument.UpdatedAt.Should().Be(ts2); // Max timestamp
        }

        [Fact]
        public void Resolve_ShouldPrioritizeHigherTimestamp_PrimitiveCollision()
        {
            // Arrange
            var oldTs = new HlcTimestamp(100, 0, "n1");
            var newTs = new HlcTimestamp(200, 0, "n2");

            var doc = CreateDoc("k1", new { val = "Old" }, oldTs);
            var op = CreateOp("k1", new { val = "New" }, newTs);

            // Act - Remote is newer
            var result1 = _resolver.Resolve(doc, op);
            result1.MergedDocument.Content.GetProperty("val").GetString().Should().Be("New");

            // Act - Local is newer (simulating outdated remote op)
            var docNew = CreateDoc("k1", new { val = "Correct" }, newTs);
            var opOld = CreateOp("k1", new { val = "Stale" }, oldTs);
            
            var result2 = _resolver.Resolve(docNew, opOld);
            result2.MergedDocument.Content.GetProperty("val").GetString().Should().Be("Correct");
        }

        [Fact]
        public void Resolve_ShouldRecursivelyMergeObjects()
        {
            // Arrange
            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");

            var doc = CreateDoc("k1", new { info = new { x = 1, y = 1 } }, ts1);
            var op = CreateOp("k1", new { info = new { y = 2, z = 3 } }, ts2);

            // Act
            var result = _resolver.Resolve(doc, op);

            // Assert
            var info = result.MergedDocument.Content.GetProperty("info");
            info.GetProperty("x").GetInt32().Should().Be(1);
            info.GetProperty("y").GetInt32().Should().Be(2); // Overwritten by newer
            info.GetProperty("z").GetInt32().Should().Be(3); // Added
        }

        [Fact]
        public void Resolve_ShouldMergeArraysById()
        {
             // Arrange
            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");

            var doc = CreateDoc("k1", new { 
                items = new[] { 
                    new { id = "1", val = "A" },
                    new { id = "2", val = "B" } 
                } 
            }, ts1);

            var op = CreateOp("k1", new { 
                items = new[] { 
                    new { id = "1", val = "A-Updated" }, // Update
                    new { id = "3", val = "C" }          // Insert
                } 
            }, ts2);

            // Act
            var result = _resolver.Resolve(doc, op);

            // Assert
            Action<JsonElement> validate = (root) => {
                var items = root.GetProperty("items");
                items.GetArrayLength().Should().Be(3);
                
                // Order is not guaranteed, so find by id
                // But simplified test checking content exists
                var text = items.GetRawText();
                text.Should().Contain("A-Updated");
                text.Should().Contain("B");
                text.Should().Contain("C");
            };
            
            validate(result.MergedDocument.Content);
        }

        [Fact]
        public void Resolve_ShouldFallbackToLWW_ForPrimitiveArrays()
        {
             // Arrange
            var ts1 = new HlcTimestamp(100, 0, "n1");
            var ts2 = new HlcTimestamp(200, 0, "n2");

            var doc = CreateDoc("k1", new { tags = new[] { "a", "b" } }, ts1);
            var op = CreateOp("k1", new { tags = new[] { "c" } }, ts2);

            // Act
            var result = _resolver.Resolve(doc, op);

            // Assert
            var tags = result.MergedDocument.Content.GetProperty("tags");
            tags.GetArrayLength().Should().Be(1);
            tags[0].GetString().Should().Be("c");
        }
    }
}
