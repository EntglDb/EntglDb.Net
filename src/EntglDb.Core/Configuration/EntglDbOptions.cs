namespace EntglDb.Core.Configuration
{
    /// <summary>
    /// Configuration options for EntglDb.
    /// </summary>
    public class EntglDbOptions
    {
        /// <summary>
        /// Network configuration options.
        /// </summary>
        public NetworkOptions Network { get; set; } = new();

        /// <summary>
        /// Persistence configuration options.
        /// </summary>
        public PersistenceOptions Persistence { get; set; } = new();

        /// <summary>
        /// Synchronization configuration options.
        /// </summary>
        public SyncOptions Sync { get; set; } = new();

        /// <summary>
        /// Logging configuration options.
        /// </summary>
        public LoggingOptions Logging { get; set; } = new();
    }

    /// <summary>
    /// Network-related configuration options.
    /// </summary>
    public class NetworkOptions
    {
        /// <summary>
        /// TCP port for peer-to-peer synchronization. Default: 5000.
        /// </summary>
        public int TcpPort { get; set; } = 5000;

        /// <summary>
        /// UDP port for peer discovery. Default: 6000.
        /// </summary>
        public int UdpPort { get; set; } = 6000;

        /// <summary>
        /// Connection timeout in milliseconds. Default: 5000ms.
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Number of retry attempts for failed network operations. Default: 3.
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Delay between retry attempts in milliseconds. Default: 1000ms.
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Enable localhost-only binding for testing. Default: false.
        /// </summary>
        public bool LocalhostOnly { get; set; } = false;
    }

    /// <summary>
    /// Persistence-related configuration options.
    /// </summary>
    public class PersistenceOptions
    {
        /// <summary>
        /// Path to the SQLite database file. Default: "entgldb.db".
        /// </summary>
        public string DatabasePath { get; set; } = "entgldb.db";

        /// <summary>
        /// Enable Write-Ahead Logging (WAL) mode for better concurrency. Default: true.
        /// </summary>
        public bool EnableWalMode { get; set; } = true;

        /// <summary>
        /// In-memory cache size in megabytes. Default: 10MB.
        /// </summary>
        public int CacheSizeMb { get; set; } = 10;

        /// <summary>
        /// Enable automatic database backup on shutdown. Default: true.
        /// </summary>
        public bool EnableAutoBackup { get; set; } = true;

        /// <summary>
        /// Path for database backups. If null, backups are disabled.
        /// </summary>
        public string? BackupPath { get; set; }

        /// <summary>
        /// SQLite busy timeout in milliseconds. Default: 5000ms.
        /// </summary>
        public int BusyTimeoutMs { get; set; } = 5000;
    }

    /// <summary>
    /// Synchronization-related configuration options.
    /// </summary>
    public class SyncOptions
    {
        /// <summary>
        /// Interval between automatic sync operations in milliseconds. Default: 5000ms.
        /// </summary>
        public int SyncIntervalMs { get; set; } = 5000;

        /// <summary>
        /// Maximum number of operations to sync in a single batch. Default: 100.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Enable offline operation queue. Default: true.
        /// </summary>
        public bool EnableOfflineQueue { get; set; } = true;

        /// <summary>
        /// Maximum size of offline queue. Default: 1000.
        /// </summary>
        public int MaxQueueSize { get; set; } = 1000;
    }

    /// <summary>
    /// Logging-related configuration options.
    /// </summary>
    public class LoggingOptions
    {
        /// <summary>
        /// Minimum log level. Default: "Information".
        /// </summary>
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// Path to log file. If null, file logging is disabled.
        /// </summary>
        public string? LogFilePath { get; set; }

        /// <summary>
        /// Maximum log file size in megabytes before rotation. Default: 10MB.
        /// </summary>
        public int MaxLogFileSizeMb { get; set; } = 10;

        /// <summary>
        /// Number of log files to retain. Default: 5.
        /// </summary>
        public int MaxLogFiles { get; set; } = 5;
    }
}
