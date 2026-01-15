using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Exceptions;

namespace EntglDb.Core.Resilience
{
    /// <summary>
    /// Provides retry logic for transient failures.
    /// </summary>
    public class RetryPolicy
    {
        private readonly ILogger<RetryPolicy> _logger;
        private readonly int _maxAttempts;
        private readonly int _delayMs;

        public RetryPolicy(ILogger<RetryPolicy> logger, int maxAttempts = 3, int delayMs = 1000)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxAttempts = maxAttempts;
            _delayMs = delayMs;
        }

        /// <summary>
        /// Executes an operation with retry logic.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation, 
            string operationName,
            CancellationToken cancellationToken = default)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    _logger.LogDebug("Executing {Operation} (attempt {Attempt}/{Max})", 
                        operationName, attempt, _maxAttempts);
                    
                    return await operation();
                }
                catch (Exception ex) when (attempt < _maxAttempts && IsTransient(ex))
                {
                    lastException = ex;
                    var delay = _delayMs * attempt; // Exponential backoff
                    
                    _logger.LogWarning(ex, 
                        "Operation {Operation} failed (attempt {Attempt}/{Max}). Retrying in {Delay}ms...", 
                        operationName, attempt, _maxAttempts, delay);
                    
                    await Task.Delay(delay, cancellationToken);
                }
            }

            _logger.LogError(lastException, 
                "Operation {Operation} failed after {Attempts} attempts", 
                operationName, _maxAttempts);
            
            throw new EntglDbException("RETRY_EXHAUSTED", 
                $"Operation '{operationName}' failed after {_maxAttempts} attempts", 
                lastException!);
        }

        /// <summary>
        /// Executes an operation with retry logic (void return).
        /// </summary>
        public async Task ExecuteAsync(
            Func<Task> operation, 
            string operationName,
            CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true;
            }, operationName, cancellationToken);
        }

        private static bool IsTransient(Exception ex)
        {
            // Network errors are typically transient
            if (ex is NetworkException or System.Net.Sockets.SocketException or System.IO.IOException)
                return true;

            // Timeout errors are transient
            if (ex is Exceptions.TimeoutException or OperationCanceledException)
                return true;

            return false;
        }
    }
}
