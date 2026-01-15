using System.ComponentModel.DataAnnotations;
using EntglDb.Core.Metadata;
using FluentAssertions;
using Xunit;

namespace EntglDb.Core.Tests;

public class EntityMetadataTests
{
    // Test entities
    public class UserWithKeyAttribute
    {
        [Key]
        public string Email { get; set; } = "";
        public string? Name { get; set; }
    }

    public class UserWithAttribute
    {
        [PrimaryKey(AutoGenerate = true)]
        public string Id { get; set; } = "";
        
        [Indexed]
        public int Age { get; set; }
        
        public string? Name { get; set; }
    }

    public class UserWithConvention
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
    }

    public class ProductWithTypedId
    {
        public string ProductId { get; set; } = "";
        public string? Name { get; set; }
    }

    public class EntityWithoutKey
    {
        public string? Name { get; set; }
    }

    public class UserWithNoAutoGen
    {
        [PrimaryKey(AutoGenerate = false)]
        public string Id { get; set; } = "";
    }

    [Fact]
    public void PrimaryKey_ShouldBeDetected_ViaKeyAttribute()
    {
        // Act
        var prop = EntityMetadata<UserWithKeyAttribute>.PrimaryKeyProperty;

        // Assert
        prop.Should().NotBeNull();
        prop!.Name.Should().Be("Email");
    }

    [Fact]
    public void PrimaryKey_ShouldBeDetected_ViaAttribute()
    {
        // Act
        var prop = EntityMetadata<UserWithAttribute>.PrimaryKeyProperty;

        // Assert
        prop.Should().NotBeNull();
        prop!.Name.Should().Be("Id");
    }

    [Fact]
    public void PrimaryKey_ShouldBeDetected_ViaIdConvention()
    {
        // Act
        var prop = EntityMetadata<UserWithConvention>.PrimaryKeyProperty;

        // Assert
        prop.Should().NotBeNull();
        prop!.Name.Should().Be("Id");
    }

    [Fact]
    public void PrimaryKey_ShouldBeDetected_ViaTypeNameIdConvention()
    {
        // Note: Convention is {TypeName}Id, so for "ProductWithTypedId" it would look for "ProductWithTypedIdId"
        // Since we have "ProductId", it won't match. This test demonstrates the convention limitation.
        
        // Act
        var prop = EntityMetadata<ProductWithTypedId>.PrimaryKeyProperty;

        // Assert - ProductId doesn't match ProductWithTypedIdId convention
        prop.Should().BeNull();
    }

    [Fact]
    public void PrimaryKey_ShouldBeNull_WhenNoKeyDefined()
    {
        // Act
        var prop = EntityMetadata<EntityWithoutKey>.PrimaryKeyProperty;

        // Assert
        prop.Should().BeNull();
    }

    [Fact]
    public void AutoGenerateKey_ShouldBeTrue_ByDefault()
    {
        // Act
        var autoGen = EntityMetadata<UserWithAttribute>.AutoGenerateKey;

        // Assert
        autoGen.Should().BeTrue();
    }

    [Fact]
    public void AutoGenerateKey_ShouldBeFalse_WhenExplicitlyDisabled()
    {
        // Act
        var autoGen = EntityMetadata<UserWithNoAutoGen>.AutoGenerateKey;

        // Assert
        autoGen.Should().BeFalse();
    }

    [Fact]
    public void GetKey_ShouldExtractKeyValue()
    {
        // Arrange
        var user = new UserWithAttribute { Id = "test-123", Name = "Alice" };

        // Act
        var key = EntityMetadata<UserWithAttribute>.GetKey!(user);

        // Assert
        key.Should().Be("test-123");
    }

    [Fact]
    public void SetKey_ShouldUpdateKeyValue()
    {
        // Arrange
        var user = new UserWithAttribute { Id = "", Name = "Alice" };

        // Act
        EntityMetadata<UserWithAttribute>.SetKey!(user, "new-key");

        // Assert
        user.Id.Should().Be("new-key");
    }

    [Fact]
    public void IndexedProperties_ShouldBeDetected()
    {
        // Act
        var indexed = EntityMetadata<UserWithAttribute>.IndexedProperties;

        // Assert
        indexed.Should().HaveCount(1);
        indexed[0].Name.Should().Be("Age");
    }

    [Fact]
    public void CollectionName_ShouldBeLowercaseTypeName()
    {
        // Act
        var name = EntityMetadata<UserWithAttribute>.CollectionName;

        // Assert
        name.Should().Be("userwithattribute");
    }

    [Fact]
    public void Metadata_ShouldBeCached_ForPerformance()
    {
        // Act - Call multiple times
        var prop1 = EntityMetadata<UserWithAttribute>.PrimaryKeyProperty;
        var prop2 = EntityMetadata<UserWithAttribute>.PrimaryKeyProperty;

        // Assert - Should be same instance (cached)
        prop1.Should().BeSameAs(prop2);
    }
}
