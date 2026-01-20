using System;
using System.IO;

namespace EntglDb.Persistence.Sqlite
{
    /// <summary>
    /// Configuration options for SQLite persistence layer.
    /// </summary>
    public class SqlitePersistenceOptions
    {
        /// <summary>
        /// Base directory path for database storage.
        /// If null, defaults to {AppData}/EntglDb
        /// </summary>
        public string? BasePath { get; set; }

        /// <summary>
        /// If true, uses per-collection tables (Documents_{Collection}, Oplog_{Collection})
        /// If false, uses single Documents/Oplog tables with Collection column (legacy)
        /// Default: true
        /// </summary>
        public bool UsePerCollectionTables { get; set; } = true;

        /// <summary>
        /// Custom database filename template. 
        /// Supports {NodeId} placeholder.
        /// Default: "node-{NodeId}.db"
        /// </summary>
        public string DatabaseFilenameTemplate { get; set; } = "node-{NodeId}.db";

        /// <summary>
        /// Gets the default base path for database storage.
        /// </summary>
        internal static string DefaultBasePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EntglDb"
        );
    }
}
