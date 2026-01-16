using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntglDb.Core
{
    public class EntglDbMapper
    {
        private static readonly Lazy<EntglDbMapper> _instance = new Lazy<EntglDbMapper>(() => new EntglDbMapper());
        public static EntglDbMapper Global => _instance.Value;

        private readonly ConcurrentDictionary<Type, EntityBuilder> _builders = new ConcurrentDictionary<Type, EntityBuilder>();

        public EntityBuilder<T> Entity<T>()
        {
            return (EntityBuilder<T>)_builders.GetOrAdd(typeof(T), t => (EntityBuilder)new EntityBuilder<T>());
        }

        public EntityBuilder GetEntityBuilder(Type type)
        {
            return _builders.GetOrAdd(type, t => (EntityBuilder)Activator.CreateInstance(typeof(EntityBuilder<>).MakeGenericType(t))!);
        }
    }

    public abstract class EntityBuilder
    {
        public string? CollectionName { get; set; }
        public List<string> IndexedProperties { get; } = new List<string>();
        // Add more config options as needed (Ignored properties, custom mapping, etc.)
    }

    public class EntityBuilder<T> : EntityBuilder
    {
        public EntityBuilder<T> Id(Expression<Func<T, object>> expression)
        {
            // Configure ID mapping logic if we want to override [PrimaryKey] attribute
            return this;
        }

        public EntityBuilder<T> Index(Expression<Func<T, object>> expression)
        {
            var member = GetMemberName(expression);
            if(member != null && !IndexedProperties.Contains(member))
            {
                IndexedProperties.Add(member);
            }
            return this;
        }

        public EntityBuilder<T> Collection(string name)
        {
            CollectionName = name;
            return this;
        }

        private string? GetMemberName(Expression<Func<T, object>> expression)
        {
             // Simple extraction logic
             if (expression.Body is MemberExpression m) return m.Member.Name;
             if (expression.Body is UnaryExpression u && u.Operand is MemberExpression um) return um.Member.Name;
             return null;
        }
    }
}
