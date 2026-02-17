using BLite.Core.Collections;
using BLite.Core.Metadata;
using BLite.Core.Storage;
using EntglDb.Persistence.BLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntglDb.Sample.Shared;

public partial class SampleDbContext : EntglDocumentDbContext
{
    public DocumentCollection<string, User> Users { get; private set; }
    public DocumentCollection<string, TodoList> TodoLists { get; private set; }

    /// <summary>
    /// Initializes a new instance of the SampleDbContext class using the specified database file path.
    /// </summary>
    /// <param name="databasePath">The file system path to the database file. Cannot be null or empty.</param>
    public SampleDbContext(string databasePath) : base(databasePath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the SampleDbContext class using the specified database file path and page file
    /// configuration.
    /// </summary>
    /// <param name="databasePath">The file system path to the database file. Cannot be null or empty.</param>
    /// <param name="config">The configuration settings for the page file. Cannot be null.</param>
    public SampleDbContext(string databasePath, PageFileConfig config) : base(databasePath, config)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>()
            .ToCollection("Users")
            .HasKey(u => u.Id);
            
        modelBuilder.Entity<TodoList>()
            .ToCollection("TodoLists")
            .HasKey(t => t.Id);
    }
}
