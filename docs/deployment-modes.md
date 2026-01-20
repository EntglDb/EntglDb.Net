# EntglDb Deployment Modes

EntglDb.AspNet supports two deployment modes for different scenarios:

## Single-Cluster Mode

**Best for:**
- Dedicated database servers
- Simple deployments
- Development/testing environments
- Small-to-medium applications

### Architecture

```
┌─────────────────────┐
│   ASP.NET Server    │
│                     │
│  ┌───────────────┐  │
│  │   EntglDb     │  │  
│  │  (1 cluster)  │  │
│  └───────────────┘  │
│         │           │
│    [TCP:5001]       │
└─────────────────────┘
```

### Configuration

```csharp
builder.Services.AddEntglDbAspNetSingleCluster(options =>
{
    options.NodeId = "server-01";
    options.TcpPort = 5001;
    options.RequireAuthentication = false; // or true for production
});
```

### Characteristics

- **One database per server instance**
- **Simple configuration**
- **Easy to scale horizontally** (multiple servers = multiple databases)
- **No port collision issues**
- **Recommended for most use cases**

## Multi-Cluster Mode

**Best for:**
- Multi-tenant SaaS applications
- Shared hosting environments
- Cost optimization (fewer server instances)
- Cluster management servers

### Architecture

```
┌──────────────────────────────┐
│      ASP.NET Server          │
│                              │
│  ┌────────┐  ┌────────┐     │
│  │Cluster1│  │Cluster2│     │
│  │(Tenant1)│  │(Tenant2)│     │
│  └────────┘  └────────┘     │
│      │           │           │
│ [TCP:5001] [TCP:5002]        │
│                              │
│  ┌────────┐  ┌────────┐     │
│  │Cluster3│  │Cluster4│     │
│  │(Tenant3)│  │(Tenant4)│     │
│  └────────┘  └────────┘     │
│      │           │           │
│ [TCP:5003] [TCP:5004]        │
└──────────────────────────────┘
```

### Configuration

```csharp
builder.Services.AddEntglDbAspNetMultiCluster(options =>
{
    options.BasePort = 5001;
    options.ClusterCount = 10; // Serve 10 tenants
    options.RequireAuthentication = true; // Recommended
    options.OAuth2Authority = "https://auth.example.com";
    options.OAuth2Audience = "entgldb-api";
    options.NodeIdTemplate = "server-{ClusterId}";
});
```

### Characteristics

- **Multiple isolated databases per server**
- **Port allocation**: BasePort + ClusterIndex (5001, 5002, 5003...)
- **Requires cluster routing logic**
- **Authentication strongly recommended**
- **More complex deployment**

## Server Behavior

Both modes use **respond-only** operation:

✅ **What servers DO:**
- Accept incoming TCP sync connections
- Respond to sync requests
- Serve data to clients
- Maintain persistence

❌ **What servers DON'T do:**
- Initiate outbound sync
- Perform UDP discovery
- Connect to other servers automatically

This design makes servers:
- **Predictable**: No surprise network activity
- **Secure**: Controlled network surface
- **Scalable**: Stateless request handling

## Persistence Layer Compatibility

Both modes work with all persistence providers:

| Provider | Single-Cluster | Multi-Cluster | Notes |
|----------|----------------|---------------|-------|
| SQLite (Direct) | ✅ | ✅ | One DB file per cluster |
| EF Core | ✅ | ✅ | Flexible DB options |
| PostgreSQL | ✅ | ✅ | Best for production |

## Scaling Strategies

### Horizontal Scaling (Single-Cluster)

```
Load Balancer
     │
     ├─> Server 1 (Cluster A) [PostgreSQL A]
     ├─> Server 2 (Cluster B) [PostgreSQL B]
     └─> Server 3 (Cluster C) [PostgreSQL C]
```

### Vertical Scaling (Multi-Cluster)

```
Single Server
     │
     ├─> Cluster 1 [DB1]
     ├─> Cluster 2 [DB2]
     ├─> Cluster 3 [DB3]
     ...
     └─> Cluster N [DBN]
```

## Decision Matrix

| Criterion | Single-Cluster | Multi-Cluster |
|-----------|----------------|---------------|
| Setup Complexity | ⭐ Easy | ⭐⭐⭐ Complex |
| Resource Efficiency | ⭐⭐ Medium | ⭐⭐⭐ High |
| Isolation | ⭐⭐⭐ Process-level | ⭐ Logical |
| Scalability | ⭐⭐⭐ Excellent | ⭐⭐ Good |
| Operational Overhead | ⭐ Low | ⭐⭐⭐ High |

## Recommendation

**Start with Single-Cluster mode** unless you have a specific requirement for Multi-Cluster. It's simpler, more robust, and easier to scale.

Use Multi-Cluster only if:
- You're building a multi-tenant SaaS with cost constraints
- You need centralized cluster management
- You have experience managing complex deployments
