using EntglDb.Core;
using EntglDb.Core.Network;
using EntglDb.Core.Storage;
using EntglDb.Network;
using EntglDb.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => 
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
    { 
        Title = "EntglDb ASP.NET Node", 
        Version = "v0.8.6",
        Description = "A decentralized peer-to-peer database node running on ASP.NET Core. Features P2P syncing, dynamic discovery, and vector-clock based consistency."
    });
});
builder.Services.AddControllers();

// Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<EntglDbHealth>("EntglDb");

// Register Configuration Provider
builder.Services.AddSingleton<AspNetPeerNodeConfigurationProvider>();

// Configure EntglDb
builder.Services.AddDbContext<EntglDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("EntglDbConnection")), ServiceLifetime.Singleton);

builder.Services.AddEntglDbCore()
    .AddEntglDbEntityFramework()
    .AddEntglDbNetwork<AspNetPeerNodeConfigurationProvider>(useHostedService: true);

var app = builder.Build();

app.UseStaticFiles(); // Serve wwwroot for custom CSS

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EntglDb API v1");
        c.InjectStylesheet("/css/swagger-custom.css");
        c.DocumentTitle = "EntglDb Node API";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EntglDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();
app.MapHealthChecks("/health");

// API: Get Available Collections
app.MapGet("/api/collections", async (IPeerStore store) => 
{
    var collections = await store.GetCollectionsAsync();
    return Results.Ok(collections);
})
.WithName("GetCollections");

// API: Get Connected Peers
app.MapGet("/api/peers", (IDiscoveryService discovery) =>
{
    // Lists peers discovered via UDP
    var activePeers = discovery.GetActivePeers();
    return Results.Ok(activePeers);
})
.WithName("GetPeers");

// API: Get Telemetry
app.MapGet("/api/telemetry", async (IPeerStore store, EntglDb.Network.Telemetry.INetworkTelemetryService telemetry) =>
{
    var vectorClock = await store.GetVectorClockAsync();
    var collections = await store.GetCollectionsAsync();
    var counts = new Dictionary<string, int>();
    
    foreach(var col in collections)
    {
        counts[col] = await store.CountDocumentsAsync(col, null);
    }

    return Results.Ok(new 
    { 
        VectorClock = vectorClock,
        DocumentCounts = counts,
        NetworkStats = telemetry.GetSnapshot(),
        Timestamp = DateTime.UtcNow
    });
})
.WithName("GetTelemetry");

app.Run();

// Configuration Provider implementation
public class AspNetPeerNodeConfigurationProvider : IPeerNodeConfigurationProvider
{
    private readonly PeerNodeConfiguration _config;

    public AspNetPeerNodeConfigurationProvider(IConfiguration configuration)
    {
        var nodeName = configuration["EntglDb:NodeName"] ?? "AspNetCoreNode";
        var portObj = configuration["EntglDb:Port"];
        int port = int.TryParse(portObj, out int p) ? p : 4001;
        var authToken = configuration["EntglDb:AuthToken"] ?? "Test-Cluster-Key";

        _config = new PeerNodeConfiguration
        {
            NodeId = nodeName,
            TcpPort = port,
            AuthToken = authToken
        };
    }

    public event PeerNodeConfigurationChangedEventHandler? ConfigurationChanged;

    public Task<PeerNodeConfiguration> GetConfiguration()
    {
        return Task.FromResult(_config);
    }
}

public class EntglDbHealth : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly IPeerStore _store;

    public EntglDbHealth(IPeerStore store)
    {
        _store = store;
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Simple check: try to read latest timestamp
            await _store.GetLatestTimestampAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("EntglDb is reachable");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("EntglDb is unreachable", ex);
        }
    }
}
