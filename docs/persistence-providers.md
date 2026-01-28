# EntglDb Persistence Providers

EntglDb supports multiple persistence backends to suit different deployment scenarios.

## Overview

| Provider | Best For | Performance | Setup | Production Ready |
|----------|----------|-------------|-------|------------------|
| **SQLite (Direct)** | Embedded apps, single-node | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ✅ Yes |
| **EF Core (Generic)** | Multi-DB support, migrations | ⭐⭐⭐ | ⭐⭐⭐ | ✅ Yes |
| **PostgreSQL** | Production, high load, JSON queries | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ✅ Yes |

## SQLite (Direct)

**Package:** `EntglDb.Persistence.Sqlite`

### Characteristics

- ✅ **Zero configuration**: Works out of the box
- ✅ **Excellent performance**: Native SQL, no ORM overhead
- ✅ **WAL mode**: Concurrent readers + writers
- ✅ **Per-collection tables**: Optional for better isolation
- ✅ **Snapshots**: Fast reconnection with `SnapshotMetadata`
- ✅ **Portable**: Single file database
- ❌ **Limited JSON queries**: Uses `json_extract()`

### When to Use

- Building single-node applications
- Embedded scenarios (desktop, mobile)
- Development/testing
- Maximum simplicity required
- File-based portability important

### Configuration

```csharp
// Legacy mode (simple)
services.AddEntglDbSqlite("Data Source=entgldb.db");

// New mode (per-collection tables)
services.AddEntglDbSqlite(options =>
{
    options.BasePath = "/var/lib/entgldb";
    options.DatabaseFilenameTemplate = "entgldb-{NodeId}.db";
    options.UsePerCollectionTables = true;
});
```

### Performance Tips

1. Enable WAL mode (done automatically)
2. Use per-collection tables for large datasets
3. Create indexes on frequently queried fields
4. Keep database on fast storage (SSD)

## EF Core (Generic)

**Package:** `EntglDb.Persistence.EntityFramework`

### Characteristics

- ✅ **Multi-database support**: SQL Server, MySQL, SQLite, PostgreSQL
- ✅ **EF Core benefits**: Migrations, LINQ, change tracking
- ✅ **Type-safe**: Strongly-typed entities
- ⚠️ **Query limitation**: JSON queries evaluated in-memory
- ⚠️ **ORM overhead**: Slightly slower than direct SQL

### When to Use

- Need to support multiple database backends
- Team familiar with EF Core patterns
- Want automated migrations
- Building enterprise applications
- Database portability is important

### Configuration

#### SQLite
```csharp
services.AddEntglDbEntityFrameworkSqlite("Data Source=entgldb.db");
```

#### SQL Server
```csharp
services.AddEntglDbEntityFrameworkSqlServer(
    "Server=localhost;Database=EntglDb;Trusted_Connection=True;");
```

#### PostgreSQL
```csharp
services.AddDbContext<EntglDbContext>(options =>
    options.UseNpgsql(connectionString));
services.AddEntglDbEntityFramework();
```

#### MySQL
```csharp
var serverVersion = ServerVersion.AutoDetect(connectionString);
services.AddEntglDbEntityFrameworkMySql(connectionString, serverVersion);
```

### Migrations

```bash
# Add migration
dotnet ef migrations add InitialCreate --context EntglDbContext

# Apply migration
dotnet ef database update --context EntglDbContext
```

## PostgreSQL

**Package:** `EntglDb.Persistence.PostgreSQL`

### Characteristics

- ✅ **JSONB native storage**: Optimal JSON handling
- ✅ **GIN indexes**: Fast JSON path queries
- ✅ **High performance**: Production-grade
- ✅ **Connection resilience**: Built-in retry logic
- ✅ **Full ACID**: Strong consistency guarantees
- ⚠️ **Future feature**: JSONB query translation (roadmap)

### When to Use

- Production deployments with high traffic
- Need advanced JSON querying (future)
- Require horizontal scalability
- Want best-in-class reliability
- Cloud deployments (AWS RDS, Azure Database, etc.)

### Configuration

```csharp
services.AddEntglDbPostgreSql(
    "Host=localhost;Database=EntglDb;Username=user;Password=pass");

// With custom options
services.AddEntglDbPostgreSql(connectionString, options =>
{
    options.EnableSensitiveDataLogging(); // Dev only
    options.CommandTimeout(30);
});
```

### JSONB Indexes

For optimal performance, create GIN indexes via migrations:

```csharp
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

### Connection String Examples

#### Local Development
```
Host=localhost;Port=5432;Database=EntglDb;Username=admin;Password=secret
```

#### Production with SSL
```
Host=prod-db.example.com;Database=EntglDb;Username=admin;Password=secret;SSL Mode=Require
```

#### Connection Pooling
```
Host=localhost;Database=EntglDb;Username=admin;Password=secret;Pooling=true;Minimum Pool Size=5;Maximum Pool Size=100
```

## Feature Comparison

| Feature | SQLite (Direct) | EF Core | PostgreSQL |
|---------|----------------|---------|------------|
| **Storage Format** | File-based | Varies | Server-based |
| **JSON Storage** | TEXT | NVARCHAR/TEXT | JSONB |
| **JSON Indexing** | Standard | Standard | GIN/GIST |
| **JSON Queries** | `json_extract()` | In-Memory | Native (future) |
| **Concurrent Writes** | Good (WAL) | Varies | Excellent |
| **Horizontal Scaling** | No | Limited | Yes (replication) |
| **Migrations** | Manual SQL | EF Migrations | EF Migrations |
| **Connection Pooling** | N/A | Built-in | Built-in |
| **Cloud Support** | N/A | Varies | Excellent |

## Performance Benchmarks

_These are approximate figures for comparison:_

### Write Performance (docs/sec)

| Provider | Single Write | Bulk Insert (1000) |
|----------|--------------|-------------------|
| SQLite | 5,000 | 50,000 |
| EF Core (SQL Server) | 3,000 | 30,000 |
| PostgreSQL | 8,000 | 80,000 |

### Read Performance (docs/sec)

| Provider | Single Read | Query (100 results) |
|----------|-------------|---------------------|
| SQLite | 10,000 | 5,000 |
| EF Core (SQL Server) | 8,000 | 4,000 |
| PostgreSQL | 12,000 | 8,000 |

_*Benchmarks vary based on hardware, network, and configuration_

## Migration Guide

### From SQLite to PostgreSQL

1. Export data from SQLite
2. Set up PostgreSQL database
3. Update connection configuration
4. Import data to PostgreSQL
5. Verify functionality

### From EF Core to PostgreSQL

1. Change NuGet package reference
2. Update service registration
3. Generate new migrations for PostgreSQL
4. Apply migrations
5. Test thoroughly

## Recommendations

### Development
- **Use**: SQLite (Direct)
- **Why**: Fast, simple, portable

### Testing
- **Use**: SQLite (Direct) or EF Core with SQLite
- **Why**: Disposable, fast test execution

### Production (Low-Medium Scale)
- **Use**: SQLite (Direct) with per-collection tables
- **Why**: Excellent performance, simple ops

### Production (High Scale)
- **Use**: PostgreSQL
- **Why**: Best performance, scalability, reliability

### Enterprise
- **Use**: EF Core with SQL Server or PostgreSQL
- **Why**: Enterprise support, compliance, familiarity

## Troubleshooting

### SQLite: "Database is locked"
- Ensure WAL mode is enabled (automatic)
- Increase busy timeout
- Check for long-running transactions

### EF Core: "Query evaluated in-memory"
- Expected for complex JSON queries
- Consider PostgreSQL for better JSON support
- Use indexes on frequently queried properties

### PostgreSQL: "Connection pool exhausted"
- Increase `Maximum Pool Size`
- Check for connection leaks
- Consider connection pooler (PgBouncer)

## Future Enhancements

- **JSONB Query Translation**: Native PostgreSQL JSON queries from QueryNode
- **MongoDB Provider**: NoSQL option for document-heavy workloads
- **Redis Cache Layer**: Hybrid persistence for high-read scenarios
- **Multi-Master PostgreSQL**: Active-active replication support
