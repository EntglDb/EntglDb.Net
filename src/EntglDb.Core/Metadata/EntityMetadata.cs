using System;
using System.Linq;
using System.Reflection;
using System.ComponentModel.DataAnnotations;

namespace EntglDb.Core.Metadata;

/// <summary>
/// Provides cached metadata for entity types using reflection.
/// Metadata is computed once per type and cached for performance.
/// </summary>
public static class EntityMetadata<T>
{
    private static readonly Lazy<PropertyInfo?> _primaryKeyProperty;
    private static readonly Lazy<Func<T, string>?> _keyGetter;
    private static readonly Lazy<Action<T, string>?> _keySetter;
    private static readonly Lazy<PropertyInfo[]> _indexedProperties;
    private static readonly Lazy<bool> _autoGenerateKey;
    
    static EntityMetadata()
    {
        _primaryKeyProperty = new Lazy<PropertyInfo?>(() => 
        {
            // 1. Look for [PrimaryKey] attribute or standard [Key]
            var prop = typeof(T).GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<PrimaryKeyAttribute>() != null 
                                  || p.GetCustomAttribute<KeyAttribute>() != null);
            
            // 2. Convention: "Id" or "{TypeName}Id"
            if (prop == null)
            {
                prop = typeof(T).GetProperty("Id") 
                    ?? typeof(T).GetProperty($"{typeof(T).Name}Id");
            }
            
            return prop;
        });
        
        _keyGetter = new Lazy<Func<T, string>?>(() =>
        {
            var prop = PrimaryKeyProperty;
            if (prop == null) return null;
            
            return entity => prop.GetValue(entity)?.ToString() ?? "";
        });
        
        _keySetter = new Lazy<Action<T, string>?>(() =>
        {
            var prop = PrimaryKeyProperty;
            if (prop == null || !prop.CanWrite) return null;
            
            return (entity, value) =>
            {
                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(entity, value);
                }
                else if (prop.PropertyType == typeof(Guid))
                {
                    prop.SetValue(entity, Guid.Parse(value));
                }
                else
                {
                    prop.SetValue(entity, Convert.ChangeType(value, prop.PropertyType));
                }
            };
        });
        
        _indexedProperties = new Lazy<PropertyInfo[]>(() =>
            typeof(T).GetProperties()
                .Where(p => p.GetCustomAttribute<IndexedAttribute>() != null)
                .ToArray()
        );
        
        _autoGenerateKey = new Lazy<bool>(() =>
        {
            var prop = PrimaryKeyProperty;
            if (prop == null) return false;
            
            var attr = prop.GetCustomAttribute<PrimaryKeyAttribute>();
            return attr?.AutoGenerate ?? true; // Default: auto-generate
        });
    }
    
    /// <summary>
    /// Gets the primary key property for this entity type, or null if none found.
    /// </summary>
    public static PropertyInfo? PrimaryKeyProperty => _primaryKeyProperty.Value;
    
    /// <summary>
    /// Gets a function to extract the primary key value from an entity.
    /// </summary>
    public static Func<T, string>? GetKey => _keyGetter.Value;
    
    /// <summary>
    /// Gets an action to set the primary key value on an entity.
    /// </summary>
    public static Action<T, string>? SetKey => _keySetter.Value;
    
    /// <summary>
    /// Gets the properties marked with [Indexed] attribute.
    /// </summary>
    public static PropertyInfo[] IndexedProperties => _indexedProperties.Value;
    
    /// <summary>
    /// Gets whether the primary key should be auto-generated.
    /// </summary>
    public static bool AutoGenerateKey => _autoGenerateKey.Value;
    
    /// <summary>
    /// Gets the default collection name for this entity type (lowercase type name).
    /// </summary>
    public static string CollectionName => typeof(T).Name.ToLowerInvariant();
}
