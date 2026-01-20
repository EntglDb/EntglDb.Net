# EntglDb.Persistence.EntityFramework

Entity Framework Core persistence provider for EntglDb, supporting SQL Server, PostgreSQL, MySQL, and SQLite.

## Features

- ✅ **Multi-Database Support**: Works with SQL Server, PostgreSQL, MySQL, and SQLite
- ✅ **EF Core Integration**: Leverage familiar EF Core patterns and migrations
- ✅ **Transaction Support**: ACID-compliant batch operations
- ✅ **Conflict Resolution**: Built-in Last-Write-Wins strategy
- ✅ **Type-Safe**: Strongly-typed entity models

## Installation

```bash
dotnet add package EntglDb.Persistence.EntityFramework
```

## Quick Start

### SQLite

```csharp
services.AddEntglDbEntityFrameworkSqlite("Data Source=entgldb.db");
```

### SQL Server

```csharp
services.AddEntglDbEntityFrameworkSqlServer(
    "Server=localhost;Database=EntglDb;Trusted_Connection=True;");
```

### PostgreSQL

```csharp
services.AddDbContext<EntglDbContext>(options =>
    options.UseNpgsql("Host=localhost;Database=EntglDb;Username=user;Password=pass"));
services.AddEntglDbEntityFramework();
```

### MySQL

```csharp
var serverVersion = ServerVersion.AutoDetect(connectionString);
services.AddEntglDbEntityFrameworkMySql(connectionString, serverVersion);
```

## Migrations

Create and apply migrations using EF Core tools:

```bash
# Install EF Core tools
dotnet tool install --global dotnet-ef

# Add initial migration
dotnet ef migrations add InitialCreate --context EntglDbContext

# Update database
dotnet ef database update --context EntglDbContext
```

## Custom Configuration

```csharp
services.AddDbContext<EntglDbContext>(options =>
{
    options.UseSqlServer(connectionString);
    options.EnableSensitiveDataLogging(); // Dev only
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

services.AddEntglDbEntityFramework();
```

## Performance Considerations

- **Query Expressions**: Evaluated in-memory. For production workloads with complex JSON queries, use PostgreSQL provider with JSONB support
- **Indexing**: Create indexes via migrations for better performance
- **Batching**: EF Core automatically batches multiple operations

## Comparison with Other Providers

| Feature | SQLite (Direct) | EF Core | PostgreSQL (EF) |
|---------|----------------|---------|-----------------|
| Setup Complexity | Low | Medium | Medium |
| Performance | Excellent | Good | Excellent |
| JSON Queries | SQL Functions | In-Memory | JSONB Native |
| Migrations | Manual | Automated | Automated |
| Multi-DB Support | No | Yes | No |

## When to Use

**Use EF Core when:**
- You need multi-database support
- You want automated migrations
- Your team is familiar with EF Core patterns
- You're building an enterprise application

**Use Direct SQLite when:**
- You need maximum performance
- You're building a single-node app
- Database portability isn't required

**Use PostgreSQL when:**
- You need advanced JSON querying
- You're running in production with high load
- JSONB indexes are important

## License

MIT
