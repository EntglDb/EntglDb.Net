# EntglDb.AspNet

ASP.NET Core integration for EntglDb with health checks, hosted services, and multi-cluster support.

## Features

- ✅ **Single & Multi-Cluster Modes**: Flexible deployment options
- ✅ **Health Checks**: Built-in health endpoints for monitoring
- ✅ **Hosted Services**: Automatic lifecycle management
- ✅ **OAuth2 JWT Support**: Optional authentication for secure sync
- ✅ **Respond-Only Mode**: Server doesn't initiate outbound sync

## Installation

```bash
dotnet add package EntglDb.AspNet
```

## Quick Start - Single Cluster

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add EntglDb with SQLite persistence
builder.Services.AddEntglDbSqlite(options =>
{
    options.BasePath = "/var/lib/entgldb";
    options.UsePerCollectionTables = true;
});

// Add ASP.NET integration (single-cluster mode)
builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.NodeId = "server-01";
    options.TcpPort = 5001;
    options.RequireAuthentication = false;
});

var app = builder.Build();

// Map health check endpoint
app.MapHealthChecks("/health");

app.Run();
```

## Multi-Cluster Mode

```csharp
builder.Services.AddEntglDbAspNetMultiCluster(options =>
{
    options.BasePort = 5001;
    options.ClusterCount = 10;
    options.RequireAuthentication = true;
    options.OAuth2Authority = "https://auth.example.com";
    options.OAuth2Audience = "entgldb-api";
    options.NodeIdTemplate = "server-{ClusterId}";
});
```

## With PostgreSQL

```csharp
builder.Services.AddEntglDbPostgreSql(
    "Host=localhost;Database=EntglDb;Username=user;Password=pass");

builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.NodeId = "prod-server-01";
    options.TcpPort = 5001;
});
```

## OAuth2 Authentication

When authentication is enabled, clients must provide valid JWT tokens:

```csharp
builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.RequireAuthentication = true;
    options.OAuth2Authority = "https://auth.example.com";
    options.OAuth2Audience = "entgldb-api";
});
```

## Health Checks

EntglDb registers health checks that verify:
- Database connectivity
- Latest timestamp retrieval

Access health status:

```bash
curl http://localhost:5000/health
```

Response:
```json
{
  "status": "Healthy",
  "results": {
    "entgldb": {
      "status": "Healthy",
      "description": "EntglDb is healthy. Latest timestamp: 1234567890"
    }
  }
}
```

## Deployment Modes

### Single-Cluster Mode

Best for:
- Dedicated database servers
- Simple deployments
- Development/testing environments

Each server instance manages one database/cluster.

### Multi-Cluster Mode

Best for:
- Multi-tenant SaaS applications
- Shared hosting environments
- Cloud deployments

One server instance can manage multiple isolated databases/clusters, each on a different port.

## Server Behavior

EntglDb ASP.NET servers operate in "respond-only" mode:
- ✅ Accept incoming sync connections
- ✅ Respond to sync requests
- ❌ Do NOT initiate outbound sync
- ❌ Do NOT perform UDP discovery

This design makes servers:
- **Predictable**: No surprise network activity
- **Secure**: Controlled network surface
- **Scalable**: Stateless request handling

## Configuration Options

### SingleClusterOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| NodeId | string | MachineName | Unique node identifier |
| TcpPort | int | 5001 | TCP port for sync |
| EnableUdpDiscovery | bool | false | Enable UDP discovery |
| RequireAuthentication | bool | false | Require OAuth2 JWT |
| OAuth2Authority | string? | null | OAuth2 authority URL |
| OAuth2Audience | string? | null | Expected audience claim |

### MultiClusterOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| BasePort | int | 5001 | Starting port number |
| ClusterCount | int | 1 | Number of clusters |
| RequireAuthentication | bool | true | Require OAuth2 JWT |
| OAuth2Authority | string? | null | OAuth2 authority URL |
| OAuth2Audience | string? | null | Expected audience claim |
| NodeIdTemplate | string | "{ClusterId}" | Node ID template |

## Monitoring & Observability

EntglDb logs important events:

```
info: EntglDb.AspNet.HostedServices.TcpSyncServerHostedService[0]
      Starting TCP Sync Server...
info: EntglDb.AspNet.Services.NoOpDiscoveryService[0]
      NoOpDiscoveryService started (passive mode - no UDP discovery)
info: EntglDb.AspNet.HealthChecks.EntglDbHealthCheck[0]
      EntglDb is healthy. Latest timestamp: 1234567890
```

## Production Checklist

- [ ] Use PostgreSQL or SQL Server (not SQLite) for production
- [ ] Enable authentication in multi-tenant scenarios
- [ ] Configure health checks for load balancer
- [ ] Set up proper logging and monitoring
- [ ] Use connection pooling for database
- [ ] Configure proper firewall rules for TCP port
- [ ] Use HTTPS for OAuth2 authority
- [ ] Set unique NodeId per instance
- [ ] Test failover scenarios

## License

MIT
