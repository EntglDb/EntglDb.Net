# EntglDb.Persistence.PostgreSQL

PostgreSQL persistence provider for EntglDb with native JSONB support and GIN indexes.

## Features

- ✅ **JSONB Native Storage**: Stores document content as PostgreSQL JSONB
- ✅ **GIN Indexes**: Supports fast JSON path queries
- ✅ **Connection Resilience**: Built-in retry logic for transient failures
- ✅ **High Performance**: Optimized for production workloads
- ✅ **Full ACID**: Leverages PostgreSQL's transaction guarantees

## Installation

```bash
dotnet add package EntglDb.Persistence.PostgreSQL
```

## Quick Start

```csharp
services.AddEntglDbPostgreSql(
    "Host=localhost;Database=EntglDb;Username=user;Password=pass");
```

## Advanced Configuration

```csharp
services.AddEntglDbPostgreSql(connectionString, options =>
{
    options.EnableSensitiveDataLogging(); // Dev only
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    options.CommandTimeout(30); // 30 second timeout
});
```

## Migrations

Create and apply migrations using EF Core tools:

```bash
# Install EF Core tools
dotnet tool install --global dotnet-ef

# Add initial migration
dotnet ef migrations add InitialCreate --context PostgreSqlDbContext

# Update database
dotnet ef database update --context PostgreSqlDbContext
```

## JSONB Indexes

For optimal performance, create GIN indexes on JSON columns via migrations:

```csharp
// In your migration file:
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        CREATE INDEX IF NOT EXISTS IX_Documents_ContentJson_gin 
        ON ""Documents"" USING GIN (""ContentJson"" jsonb_path_ops);
        
        CREATE INDEX IF NOT EXISTS IX_Oplog_PayloadJson_gin 
        ON ""Oplog"" USING GIN (""PayloadJson"" jsonb_path_ops);
    ");
}
```

## JSONB Queries (Future)

PostgreSQL provider will support native JSONB queries:

```csharp
// Future feature - not yet implemented
var docs = await store.QueryDocumentsAsync("users", 
    new PropertyEquals("status", "active"));
// Translates to: WHERE ContentJson @> '{"status": "active"}'
```

## Connection String Format

```
Host=localhost;Port=5432;Database=EntglDb;Username=admin;Password=secret
```

### With SSL

```
Host=prod-db.example.com;Database=EntglDb;Username=admin;Password=secret;SSL Mode=Require
```

### Connection Pooling

```
Host=localhost;Database=EntglDb;Username=admin;Password=secret;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100
```

## Performance Tips

1. **Enable Connection Pooling**: Always use pooling in production
2. **Create GIN Indexes**: Essential for fast JSON queries
3. **Monitor Query Performance**: Use `EXPLAIN ANALYZE` on complex queries
4. **Tune PostgreSQL**: Adjust `shared_buffers`, `work_mem` for your workload

## Comparison with Other Providers

| Feature | SQLite (Direct) | EF Core Generic | PostgreSQL |
|---------|----------------|-----------------|------------|
| JSON Storage | TEXT | NVARCHAR/TEXT | JSONB |
| JSON Queries | json_extract | In-Memory | Native (@>, ?, etc) |
| Indexing | Standard | Standard | GIN/GIST |
| Scalability | Single Node | Medium | High |
| Production Ready | Yes | Yes | Yes |

## When to Use PostgreSQL

**Use PostgreSQL when:**
- You need advanced JSON querying capabilities
- Running high-traffic production workloads
- Require horizontal scaling (with replication)
- Need complex indexing strategies
- Want full ACID compliance with high concurrency

**Use SQLite when:**
- Building single-node applications
- Maximum simplicity is priority
- Embedded scenarios

**Use EF Core Generic when:**
- You need multi-database support
- Your team prefers ORM patterns

## License

MIT
