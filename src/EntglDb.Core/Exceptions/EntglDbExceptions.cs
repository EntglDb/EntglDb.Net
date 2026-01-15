using System;

namespace EntglDb.Core.Exceptions
{
    /// <summary>
    /// Base exception for all EntglDb-related errors.
    /// </summary>
    public class EntglDbException : Exception
    {
        /// <summary>
        /// Error code for programmatic error handling.
        /// </summary>
        public string ErrorCode { get; }

        public EntglDbException(string errorCode, string message) 
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public EntglDbException(string errorCode, string message, Exception innerException) 
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Exception thrown when network operations fail.
    /// </summary>
    public class NetworkException : EntglDbException
    {
        public NetworkException(string message) 
            : base("NETWORK_ERROR", message) { }

        public NetworkException(string message, Exception innerException) 
            : base("NETWORK_ERROR", message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when persistence operations fail.
    /// </summary>
    public class PersistenceException : EntglDbException
    {
        public PersistenceException(string message) 
            : base("PERSISTENCE_ERROR", message) { }

        public PersistenceException(string message, Exception innerException) 
            : base("PERSISTENCE_ERROR", message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when synchronization operations fail.
    /// </summary>
    public class SyncException : EntglDbException
    {
        public SyncException(string message) 
            : base("SYNC_ERROR", message) { }

        public SyncException(string message, Exception innerException) 
            : base("SYNC_ERROR", message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when configuration is invalid.
    /// </summary>
    public class ConfigurationException : EntglDbException
    {
        public ConfigurationException(string message) 
            : base("CONFIG_ERROR", message) { }
    }

    /// <summary>
    /// Exception thrown when database corruption is detected.
    /// </summary>
    public class DatabaseCorruptionException : PersistenceException
    {
        public DatabaseCorruptionException(string message) 
            : base(message) { }

        public DatabaseCorruptionException(string message, Exception innerException) 
            : base(message, innerException) { }
    }

    /// <summary>
    /// Exception thrown when a timeout occurs.
    /// </summary>
    public class TimeoutException : EntglDbException
    {
        public TimeoutException(string operation, int timeoutMs) 
            : base("TIMEOUT_ERROR", $"Operation '{operation}' timed out after {timeoutMs}ms") { }
    }
}
