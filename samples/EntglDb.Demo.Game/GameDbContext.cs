using BLite.Core;
using BLite.Core.Collections;
using BLite.Core.Metadata;

namespace EntglDb.Demo.Game;

public partial class GameDbContext : DocumentDbContext
{
    public DocumentCollection<string, Hero> Heroes { get; private set; } = null!;
    public DocumentCollection<string, BattleLog> BattleLogs { get; private set; } = null!;

    public GameDbContext(string databasePath) : base(databasePath)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Hero>()
            .ToCollection("Heroes")
            .HasKey(h => h.Id);

        modelBuilder.Entity<BattleLog>()
            .ToCollection("BattleLogs")
            .HasKey(b => b.Id);
    }
}
