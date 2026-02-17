using System;

namespace EntglDb.Core.Storage;

public class CorruptDatabaseException : Exception
{
    public CorruptDatabaseException(string message, Exception innerException) : base(message, innerException) { }
}
